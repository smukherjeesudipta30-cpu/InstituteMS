using System.Globalization;
using System.Security.Cryptography;
using InstituteMS.Application.Notifications;
using InstituteMS.Application.Auth;
using InstituteMS.Application.Common;
using InstituteMS.Application.Common.Interfaces;
using InstituteMS.Domain.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using InstituteMS.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Replaces the legacy <c>AccountController.Login</c> + <c>Repository.GetUserDetails</c>
/// pair: one indexed query and a BCrypt verify instead of loading every user and
/// comparing plain text in memory (defects D3, D8).
/// </summary>
public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _tokens;
    private readonly JwtSettings _settings;
    private readonly ILogger<AuthService> _logger;
    private readonly IStudentMailer _mailer;

    private const string InvalidCredentials = "Invalid email or password.";

    public AuthService(
        AppDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService tokens,
        IOptions<JwtSettings> settings,
        IStudentMailer mailer,
        ILogger<AuthService> logger)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _settings = settings.Value;
        _mailer = mailer;
        _logger = logger;
    }

    public async Task<Result<AuthResultDto>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim();

        // Four email addresses are shared by two accounts each in the migrated data
        // (duplicate registrations in the legacy system). Ordering makes the choice
        // deterministic instead of leaving it to whatever the query planner returns:
        // active first, then the most recently registered.
        var candidates = await _db.Users
            .Where(u => u.Email == email)
            .OrderByDescending(u => u.IsActive)
            .ThenByDescending(u => u.RegisterDate)
            .ThenByDescending(u => u.Id)
            .ToListAsync(ct);

        if (candidates.Count > 1)
        {
            _logger.LogWarning(
                "Email {Email} is shared by {Count} accounts (ids {Ids}); signing in as {ChosenId}.",
                email, candidates.Count, string.Join(", ", candidates.Select(u => u.Id)), candidates[0].Id);
        }

        var user = candidates.FirstOrDefault();

        if (user is null)
        {
            // Burn the same amount of time as a real verify so the response
            // does not reveal whether the address exists.
            _hasher.Verify(request.Password, null);
            return Result<AuthResultDto>.Failure(InvalidCredentials, ResultStatus.Unauthorized);
        }

        var now = DateTime.UtcNow;

        if (user.IsLockedOut(now))
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((user.LockoutEnd!.Value - now).TotalMinutes));
            return Result<AuthResultDto>.Failure(
                $"Account locked after too many failed attempts. Try again in {minutes} minute(s).",
                ResultStatus.TooManyRequests);
        }

        if (!_hasher.Verify(request.Password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= _settings.MaxFailedAttempts)
            {
                user.LockoutEnd = now.AddMinutes(_settings.LockoutMinutes);
                user.FailedLoginCount = 0;
                _logger.LogWarning("Account {UserId} locked out until {LockoutEnd}", user.Id, user.LockoutEnd);
            }

            await _db.SaveChangesAsync(ct);
            return Result<AuthResultDto>.Failure(InvalidCredentials, ResultStatus.Unauthorized);
        }

        if (!user.IsActive)
            return Result<AuthResultDto>.Failure("This account is disabled. Contact the administrator.", ResultStatus.Forbidden);

        if (user.EndDate is { } endDate && endDate < DateOnly.FromDateTime(now))
            return Result<AuthResultDto>.Failure("This account has expired. Contact the administrator.", ResultStatus.Forbidden);

        var (accessToken, expiresIn) = _tokens.CreateAccessToken(user);
        var refreshToken = _tokens.CreateRefreshToken();

        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        user.LastLoginOn = now;
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = now.AddDays(_settings.RefreshTokenDays);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserId} ({Role}) signed in", user.Id, user.Roles);

        return Result<AuthResultDto>.Success(
            new AuthResultDto(accessToken, refreshToken, expiresIn, ToDto(user)));
    }

    public async Task<Result<AuthResultDto>> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.RefreshToken == request.RefreshToken, ct);

        if (user is null || user.RefreshTokenExpiry is null || user.RefreshTokenExpiry <= now)
            return Result<AuthResultDto>.Failure("Session expired. Please sign in again.", ResultStatus.Unauthorized);

        if (!user.IsActive)
            return Result<AuthResultDto>.Failure("This account is disabled.", ResultStatus.Forbidden);

        var (accessToken, expiresIn) = _tokens.CreateAccessToken(user);

        // Rotate the refresh token on every use so a stolen one is single-shot.
        var refreshToken = _tokens.CreateRefreshToken();
        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = now.AddDays(_settings.RefreshTokenDays);
        await _db.SaveChangesAsync(ct);

        return Result<AuthResultDto>.Success(
            new AuthResultDto(accessToken, refreshToken, expiresIn, ToDto(user)));
    }

    public async Task LogoutAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return;

        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Result<AuthUserDto>> GetCurrentUserAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);

        return user is null
            ? Result<AuthUserDto>.Failure("User not found.", ResultStatus.NotFound)
            : Result<AuthUserDto>.Success(ToDto(user));
    }

    public async Task<Result<bool>> ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return Result<bool>.Failure("User not found.", ResultStatus.NotFound);

        if (!_hasher.Verify(request.CurrentPassword, user.PasswordHash))
            return Result<bool>.Failure("Current password is incorrect.", ResultStatus.Unauthorized);

        user.PasswordHash = _hasher.Hash(request.NewPassword);

        // Whatever brought them here, the password is now one they chose themselves,
        // so the forced-reset hold is satisfied and lifts.
        user.MustChangePassword = false;

        user.ModifiedOn = DateTime.UtcNow;
        user.ModifiedBy = user.Email;

        // Force every other session to re-authenticate.
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;

        await _db.SaveChangesAsync(ct);

        await NotifyAdminOfPasswordChangeAsync(user, request.NewPassword, ct);

        return Result<bool>.Success(true);
    }

    private const int ResetCodeValidMinutes = 15;
    private const int MaxResetCodeAttempts = 5;

    public async Task<Result<bool>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var email = request.Email.Trim();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        /* Whether that address belongs to anyone is not this response's to reveal —
           an attacker who can tell "no such account" from "code sent" gets a free way
           to enumerate every login at the institute. Success either way; the
           difference is only in what happens behind it. */
        if (user is null || !user.IsActive)
            return Result<bool>.Success(true);

        var roles = UserRoles.Split(user.Roles);

        /* Users.Email is the sign-in id, not necessarily an inbox anyone reads — a
           student's is often a generated portal address, and staff sign in with
           whatever address was set up for them, which is not always the one they
           actually check (Teacher and Receptionist both carry their own
           PersonalEmail, separate from LoginId, for exactly this reason). Every
           role with a personal address on file gets the code there instead; only
           when there is none does it fall back to the sign-in id itself. */
        var target = email;

        if (roles.Contains(UserRoles.Student, StringComparer.OrdinalIgnoreCase))
        {
            var personalEmail = await _db.Students
                .AsNoTracking()
                .Where(s => s.AvijanLoginEmail == user.Email)
                .OrderByDescending(s => s.Id)
                .Select(s => s.PersonalEmail)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(personalEmail))
                target = personalEmail;
        }
        else if (roles.Contains(UserRoles.Teacher, StringComparer.OrdinalIgnoreCase))
        {
            var personalEmail = await _db.Teachers
                .AsNoTracking()
                .Where(t => t.LoginId == user.Email)
                .Select(t => t.PersonalEmail)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(personalEmail))
                target = personalEmail;
        }
        else if (roles.Contains(UserRoles.Receptionist, StringComparer.OrdinalIgnoreCase))
        {
            var personalEmail = await _db.Receptionists
                .AsNoTracking()
                .Where(r => r.LoginId == user.Email)
                .Select(r => r.PersonalEmail)
                .FirstOrDefaultAsync(ct);

            if (!string.IsNullOrWhiteSpace(personalEmail))
                target = personalEmail;
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var now = DateTime.UtcNow;

        user.PasswordResetCodeHash = _hasher.Hash(code);
        user.PasswordResetCodeExpiry = now.AddMinutes(ResetCodeValidMinutes);
        user.PasswordResetAttempts = 0;

        await _db.SaveChangesAsync(ct);

        var sent = await _mailer.SendPasswordResetCodeAsync(
            new PasswordResetCodeMailRequest(user.Name ?? user.Email ?? "there", target, code, ResetCodeValidMinutes),
            user.Email, ct);

        if (!sent)
        {
            _logger.LogWarning("Password reset code could not be sent to {Email} (user {UserId}).", target, user.Id);
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        const string invalidOrExpired = "That code is invalid or has expired. Request a new one.";

        var email = request.Email.Trim();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            // Same burn-time reasoning as LoginAsync: a missing account must cost the
            // same as a wrong code, or the timing itself says which one happened.
            _hasher.Verify(request.Code, null);
            return Result<bool>.Failure(invalidOrExpired, ResultStatus.BadRequest);
        }

        var now = DateTime.UtcNow;

        if (user.PasswordResetCodeHash is null || user.PasswordResetCodeExpiry is null || user.PasswordResetCodeExpiry < now)
        {
            _hasher.Verify(request.Code, null);
            return Result<bool>.Failure(invalidOrExpired, ResultStatus.BadRequest);
        }

        if (user.PasswordResetAttempts >= MaxResetCodeAttempts)
        {
            return Result<bool>.Failure(
                "Too many wrong codes. Request a new one.", ResultStatus.TooManyRequests);
        }

        if (!_hasher.Verify(request.Code, user.PasswordResetCodeHash))
        {
            user.PasswordResetAttempts++;
            await _db.SaveChangesAsync(ct);
            return Result<bool>.Failure(invalidOrExpired, ResultStatus.BadRequest);
        }

        user.PasswordHash = _hasher.Hash(request.NewPassword);
        user.MustChangePassword = false;

        // Spent — a code is good for one reset, not for the fifteen minutes it is valid.
        user.PasswordResetCodeHash = null;
        user.PasswordResetCodeExpiry = null;
        user.PasswordResetAttempts = 0;

        user.ModifiedOn = now;
        user.ModifiedBy = user.Email;

        // Same as a normal password change: whoever else was signed in is signed out.
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;

        await _db.SaveChangesAsync(ct);

        await NotifyAdminOfPasswordChangeAsync(user, request.NewPassword, ct);

        _logger.LogInformation("User {UserId} reset their password via emailed code.", user.Id);

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Tells the office that someone changed their own password, so it can help
    /// them if they forget it later — every role, not students alone: a Teacher or
    /// Receptionist locked out is exactly as much the office's problem as a student
    /// is.
    /// </summary>
    /// <remarks>
    /// <para>Sent after the change is committed and never allowed to fail the
    /// change: the password has already been set, and the caller has been told it
    /// succeeded. <b>It carries the password in clear</b> in the dashboard row —
    /// masked by default there, an Admin reveals it deliberately per row. Only a
    /// student's change is additionally emailed (<see cref="StudentPasswordChangeMailRequest"/>):
    /// students are the ones who ring the office when they forget theirs, and a
    /// student's password is not the credential to a system with everyone else's
    /// data in it the way staff credentials are.</para>
    /// </remarks>
    private async Task NotifyAdminOfPasswordChangeAsync(
        User user, string newPassword, CancellationToken ct)
    {
        var roles = UserRoles.Split(user.Roles);
        var isStudent = roles.Contains(UserRoles.Student, StringComparer.OrdinalIgnoreCase);

        string name;
        string? courseName = null;
        string? registrationId = null;

        if (isStudent)
        {
            // The portal account and the student record are joined on the issued
            // login address, which is what dbo.Users.Email holds for a student.
            var student = await _db.Students
                .AsNoTracking()
                .Where(s => s.AvijanLoginEmail == user.Email)
                .OrderByDescending(s => s.Id)
                .Select(s => new
                {
                    s.Name,
                    s.RegistrationId,
                    CourseName = s.Course != null ? s.Course.Name : null
                })
                .FirstOrDefaultAsync(ct);

            name = student?.Name ?? user.Name ?? user.Email ?? "Unknown student";
            courseName = student?.CourseName;
            registrationId = student?.RegistrationId;
        }
        else
        {
            name = user.Name ?? user.Email ?? "Unknown user";
        }

        // Local time: the office reads these against its own clock.
        var changedAt = DateTime.Now;
        var role = UserRoles.Primary(user.Roles);

        /* Recorded before the mail is attempted, and saved on its own, so the
           dashboard shows the alert even when the mail server is unreachable. The
           email (students only) is a copy of this row, not the other way round. */
        _db.AdminNotifications.Add(new AdminNotification
        {
            Type = isStudent ? AdminNotificationTypes.StudentPasswordChange : AdminNotificationTypes.StaffPasswordChange,
            Title = isStudent ? $"{name} changed their portal password" : $"{name} ({role}) changed their password",
            StudentName = name,
            CourseName = isStudent ? courseName : role,
            RegistrationId = registrationId,
            LoginId = user.Email,
            DetailValue = newPassword,
            CreatedOn = changedAt
        });

        await _db.SaveChangesAsync(ct);

        if (!isStudent)
            return;

        await _mailer.SendStudentPasswordChangeAsync(
            new StudentPasswordChangeMailRequest(
                name,
                courseName,
                registrationId,
                user.Email,
                newPassword,
                changedAt),
            user.Email, ct);
    }

    private static AuthUserDto ToDto(User user)
    {
        // A user can hold several roles ("Receptionist & Teacher"); `Role` is the
        // most privileged one and drives the landing page, `Roles` carries them all.
        var roles = UserRoles.Split(user.Roles);
        var primary = UserRoles.Primary(user.Roles);

        return new AuthUserDto(
            user.Id,
            user.Name ?? user.Email ?? "User",
            user.Email ?? string.Empty,
            primary,
            roles.Length > 0 ? roles : [primary],
            user.LoginId,
            UserRoles.LandingRoute(user.Roles),
            user.MustChangePassword);
    }
}
