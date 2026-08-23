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

    private const string InvalidCredentials = "Invalid email or password.";

    public AuthService(
        AppDbContext db,
        IPasswordHasher hasher,
        IJwtTokenService tokens,
        IOptions<JwtSettings> settings,
        ILogger<AuthService> logger)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _settings = settings.Value;
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
        user.ModifiedOn = DateTime.UtcNow;
        user.ModifiedBy = user.Email;

        // Force every other session to re-authenticate.
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
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
            UserRoles.LandingRoute(user.Roles));
    }
}
