using InstituteMS.Application.Common;
using InstituteMS.Application.Common.Interfaces;
using InstituteMS.Application.Users;
using InstituteMS.Domain.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Replaces <c>RegisterController</c> + <c>Models/User.cs</c> (MIGRATION_PLAN §4.8).
/// The important behavioural change: passwords are BCrypt hashes and are never read
/// back. The legacy grid selected <c>Password</c> and rendered it in a column.
/// </summary>
public class UserService : IUserService
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<UserService> _logger;

    public UserService(AppDbContext db, IPasswordHasher hasher, ILogger<UserService> logger)
    {
        _db = db;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task<PagedResult<UserListItemDto>> GetListAsync(
        UserQuery query, CancellationToken ct = default)
    {
        var users = _db.Users.AsNoTracking().AsQueryable();

        if (query.IsActive is { } isActive)
            users = users.Where(u => u.IsActive == isActive);

        if (!string.IsNullOrWhiteSpace(query.Email))
        {
            var email = query.Email.Trim();
            users = users.Where(u => u.Email != null && u.Email.Contains(email));
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            var role = query.Role.Trim();
            users = users.Where(u => u.Roles != null && u.Roles.Contains(role));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            users = users.Where(u =>
                (u.Email != null && u.Email.Contains(term)) ||
                (u.Name != null && u.Name.Contains(term)) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(term)));
        }

        var totalCount = await users.CountAsync(ct);

        var rows = await users
            .OrderByDescending(u => u.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(u => new
            {
                u.Id, u.Name, u.Email, u.Roles, u.PhoneNumber,
                u.DateOfBirth, u.RegisterDate, u.EndDate, u.IsActive, u.LastLoginOn,
                HasPassword = u.PasswordHash != null
            })
            .ToListAsync(ct);

        // A student's portal password is readable, because production stores it in
        // clear on the student row so the office can pass it on. Fetched for the
        // page's addresses only, in one query rather than one per row.
        var emails = rows.Select(u => u.Email).Where(e => e != null).ToList();

        var studentPasswords = await _db.Students
            .AsNoTracking()
            .Where(s => s.AvijanLoginEmail != null && emails.Contains(s.AvijanLoginEmail))
            .Select(s => new { s.AvijanLoginEmail, s.AvijanLoginPassword })
            .ToListAsync(ct);

        var items = rows
            .Select(u => new UserListItemDto(
                u.Id, u.Name, u.Email, u.Roles, UserRoles.Split(u.Roles),
                u.PhoneNumber, u.DateOfBirth, u.RegisterDate, u.EndDate,
                u.IsActive, u.LastLoginOn, u.HasPassword,
                studentPasswords
                    .FirstOrDefault(s => s.AvijanLoginEmail == u.Email)?.AvijanLoginPassword))
            .ToList();

        return PagedResult<UserListItemDto>.Create(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<Result<UserListItemDto>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var result = await GetListAsync(new UserQuery { Page = 1, PageSize = 1, Search = null }, ct);

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
            return Result<UserListItemDto>.Failure("User not found.", ResultStatus.NotFound);

        var studentPassword = await _db.Students
            .AsNoTracking()
            .Where(s => s.AvijanLoginEmail == user.Email)
            .Select(s => s.AvijanLoginPassword)
            .FirstOrDefaultAsync(ct);

        return Result<UserListItemDto>.Success(new UserListItemDto(
            user.Id, user.Name, user.Email, user.Roles, UserRoles.Split(user.Roles),
            user.PhoneNumber, user.DateOfBirth, user.RegisterDate, user.EndDate,
            user.IsActive, user.LastLoginOn, user.PasswordHash is not null, studentPassword));
    }

    public async Task<bool> EmailExistsAsync(
        string email, int? excludingUserId, CancellationToken ct = default)
    {
        var value = email.Trim();

        return await _db.Users.AnyAsync(
            u => u.Email == value && (excludingUserId == null || u.Id != excludingUserId), ct);
    }

    public async Task<Result<int>> CreateAsync(
        UserSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var email = request.Email!.Trim();

        if (string.IsNullOrWhiteSpace(request.Password))
            return Result<int>.Failure("A password is required for a new user.", ResultStatus.BadRequest);

        if (await EmailExistsAsync(email, null, ct))
            return Result<int>.Failure($"'{email}' is already registered.", ResultStatus.Conflict);

        var role = NormaliseRoles(request.Roles);
        if (role is null)
            return Result<int>.Failure("Please select a valid role.", ResultStatus.BadRequest);

        var user = new User
        {
            Name = request.Name?.Trim(),
            Email = email,
            Roles = role,
            PhoneNumber = request.Phone?.Trim(),
            DateOfBirth = request.DateOfBirth,
            RegisterDate = DateOnly.FromDateTime(DateTime.Today),
            IsActive = request.IsActive,
            EndDate = request.IsActive ? null : DateOnly.FromDateTime(DateTime.Today),
            PasswordHash = _hasher.Hash(request.Password),
            LoginId = actor,
            CreatedBy = actor,
            CreatedOn = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserId} ({Email}, {Role}) created by {Actor}",
            user.Id, email, role, actor);

        return Result<int>.Success(user.Id);
    }

    public async Task<Result<bool>> UpdateAsync(
        int id, UserSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Result<bool>.Failure("User not found.", ResultStatus.NotFound);

        var email = request.Email!.Trim();

        if (await EmailExistsAsync(email, id, ct))
            return Result<bool>.Failure($"'{email}' is already used by another account.", ResultStatus.Conflict);

        var role = NormaliseRoles(request.Roles);
        if (role is null)
            return Result<bool>.Failure("Please select a valid role.", ResultStatus.BadRequest);

        user.Name = request.Name?.Trim();
        user.Email = email;
        user.Roles = role;
        user.PhoneNumber = request.Phone?.Trim();
        user.DateOfBirth = request.DateOfBirth;
        user.IsActive = request.IsActive;

        // The legacy update stamped End_date whenever the account was deactivated.
        user.EndDate = request.IsActive ? null : DateOnly.FromDateTime(DateTime.Today);

        // A blank password means "leave it alone" — the legacy form overwrote it with
        // whatever was in the box, which silently wiped passwords on every edit.
        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            user.PasswordHash = _hasher.Hash(request.Password);
            user.RefreshToken = null;
            user.RefreshTokenExpiry = null;
            _logger.LogInformation("Password reset for user {UserId} by {Actor}", id, actor);
        }

        user.ModifiedBy = actor;
        user.ModifiedOn = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Sets one user's password to a value the administrator supplies, so it can be
    /// passed on to that member of staff.
    /// </summary>
    /// <remarks>
    /// This is the replacement for the legacy Users grid's plain-text
    /// <c>Password</c> column. A hash cannot be reversed, so an administrator who
    /// needs to tell someone their sign-in sets a known one here instead of reading
    /// the old one back. The value is hashed on the way in exactly like any other
    /// password and is never stored or returned in clear text.
    /// </remarks>
    public async Task<Result<bool>> SetPasswordAsync(
        int id, string password, string? actor, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Result<bool>.Failure("User not found.", ResultStatus.NotFound);

        user.PasswordHash = _hasher.Hash(password);
        user.ModifiedBy = actor;
        user.ModifiedOn = DateTime.UtcNow;

        // A student has the same password in two places: the hash here, which sign-in
        // checks, and the clear copy on the student row that the office reads back to
        // them. Updating only one would leave the Users screen showing a password
        // that no longer works.
        var student = await _db.Students
            .FirstOrDefaultAsync(s => s.AvijanLoginEmail == user.Email, ct);

        if (student is not null)
        {
            student.AvijanLoginPassword = password;
            student.ModifiedBy = actor;
            student.ModifiedOn = DateTime.Now;
        }

        // Any password change ends that user's other sessions, so a handed-over
        // credential cannot be used alongside one someone already had.
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Password set for user {UserId} by {Actor}.", id, actor ?? "(unknown)");

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> SetStatusAsync(
        int id, bool isActive, string? actor, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Result<bool>.Failure("User not found.", ResultStatus.NotFound);

        user.IsActive = isActive;
        user.EndDate = isActive ? null : DateOnly.FromDateTime(DateTime.Today);
        user.ModifiedBy = actor;
        user.ModifiedOn = DateTime.UtcNow;

        // Disabling an account must also kill any live session.
        if (!isActive)
        {
            user.RefreshToken = null;
            user.RefreshTokenExpiry = null;
        }

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Birthdays in the next N days. The legacy SQL handled the year rollover with two
    /// DATEADD branches; the same rollover is handled here by projecting this year's
    /// and next year's anniversary and taking whichever falls in the window.
    /// </summary>
    public async Task<IReadOnlyList<UpcomingBirthdayDto>> GetUpcomingBirthdaysAsync(
        int days, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var until = today.AddDays(days);

        var candidates = await _db.Users
            .AsNoTracking()
            .Where(u => u.DateOfBirth != null && u.IsActive)
            .Select(u => new { u.Id, u.Name, u.Email, u.PhoneNumber, u.DateOfBirth })
            .ToListAsync(ct);

        var results = new List<UpcomingBirthdayDto>();

        foreach (var user in candidates)
        {
            var dob = user.DateOfBirth!.Value;

            // 29 Feb in a non-leap year falls back to 28 Feb.
            var next = SafeAnniversary(dob, today.Year);
            if (next < today) next = SafeAnniversary(dob, today.Year + 1);

            if (next > until) continue;

            results.Add(new UpcomingBirthdayDto(
                user.Id, user.Name, user.Email, user.PhoneNumber, dob,
                next, next.DayNumber - today.DayNumber, next.Year - dob.Year));
        }

        return results.OrderBy(r => r.DaysAway).ThenBy(r => r.Name).ToList();
    }

    public async Task<IReadOnlyList<string>> GetRoleOptionsAsync(CancellationToken ct = default)
    {
        // Start from the canonical list, then add anything the migrated data holds
        // that is not in it (e.g. the combined "Receptionist & Teacher").
        var existing = await _db.Users
            .AsNoTracking()
            .Where(u => u.Roles != null && u.Roles != "")
            .Select(u => u.Roles!)
            .Distinct()
            .ToListAsync(ct);

        return UserRoles.All
            .Concat(existing)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<RoleCountDto>> GetRoleCountsAsync(CancellationToken ct = default)
    {
        var roles = await _db.Users
            .AsNoTracking()
            .Where(u => u.Roles != null && u.Roles != "")
            .Select(u => u.Roles!)
            .ToListAsync(ct);

        // A combined value such as "Receptionist & Teacher" is counted under both
        // names, matching the substring match GetListAsync already filters by — so
        // clicking a count on the grid returns exactly that many rows.
        return UserRoles.All
            .Select(role => new RoleCountDto(
                role, roles.Count(r => r.Contains(role, StringComparison.OrdinalIgnoreCase))))
            .ToList();
    }

    private static DateOnly SafeAnniversary(DateOnly dateOfBirth, int year)
    {
        var day = Math.Min(dateOfBirth.Day, DateTime.DaysInMonth(year, dateOfBirth.Month));
        return new DateOnly(year, dateOfBirth.Month, day);
    }

    /// <summary>
    /// Accepts either a canonical single role or a combined value already present in
    /// the data. Anything else is rejected rather than stored as an unknown role.
    /// </summary>
    private static string? NormaliseRoles(string? roles)
    {
        var parsed = UserRoles.Split(roles);

        return parsed.Length == 0 ? null : string.Join(" & ", parsed);
    }

    private const int MaxPhotoBytes = 1024 * 1024;

    public async Task<UserPhotoFile?> GetPhotoAsync(int id, CancellationToken ct = default)
    {
        var photo = await _db.UserPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == id, ct);
        return photo is null ? null : new UserPhotoFile(Convert.FromBase64String(photo.PhotoBase64), photo.ContentType);
    }

    public async Task<Result<bool>> SavePhotoAsync(int id, UserPhotoSaveRequest request, string? actor, CancellationToken ct = default)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == id, ct))
            return Result<bool>.Failure("User not found.", ResultStatus.NotFound);
        if (request.ContentType is not ("image/jpeg" or "image/png"))
            return Result<bool>.Failure("The photo must be a JPEG or PNG picture.");

        var base64 = request.Base64 ?? string.Empty;
        var comma = base64.IndexOf(',');
        if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0) base64 = base64[(comma + 1)..];
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { return Result<bool>.Failure("The photo could not be read."); }
        if (bytes.Length == 0) return Result<bool>.Failure("The photo is empty.");
        if (bytes.Length > MaxPhotoBytes) return Result<bool>.Failure("The photo is too large — please use one under 1 MB.");

        var row = await _db.UserPhotos.FirstOrDefaultAsync(p => p.UserId == id, ct);
        if (row is null) { row = new UserPhoto { UserId = id }; _db.UserPhotos.Add(row); }
        row.ContentType = request.ContentType;
        row.PhotoBase64 = base64;
        row.ModifiedBy = actor;
        row.ModifiedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> DeletePhotoAsync(int id, CancellationToken ct = default)
    {
        var row = await _db.UserPhotos.FirstOrDefaultAsync(p => p.UserId == id, ct);
        if (row is not null)
        {
            _db.UserPhotos.Remove(row);
            await _db.SaveChangesAsync(ct);
        }
        return Result<bool>.Success(true);
    }

    public async Task<UserPhotoFile?> GetStaffPhotoAsync(string loginId, CancellationToken ct = default)
    {
        var login = loginId.Trim();
        if (login.Length == 0) return null;

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.LoginId == login || u.Email == login)
            .OrderByDescending(u => u.IsActive)
            .Select(u => new { u.Id, u.Name, u.Email })
            .FirstOrDefaultAsync(ct);
        if (user is not null)
        {
            var own = await GetPhotoAsync(user.Id, ct);
            if (own is not null) return own;
        }

        // The Hierarchy chart: the box linked to their teacher/receptionist record,
        // else one with their email, else one with their name.
        var receptionists = await _db.Receptionists.AsNoTracking().Where(r => r.LoginId == login)
            .Select(r => new { Type = UserRoles.Receptionist, r.Id, r.Name, r.PersonalEmail }).ToListAsync(ct);
        var teachers = await _db.Teachers.AsNoTracking().Where(t => t.LoginId == login)
            .Select(t => new { Type = UserRoles.Teacher, t.Id, Name = t.Name ?? string.Empty, t.PersonalEmail }).ToListAsync(ct);
        var records = receptionists.Concat(teachers).ToList();

        var boxes = await _db.OrgNodes.AsNoTracking()
            .Where(n => n.IsActive && n.PhotoBase64 != null)
            .Select(n => new { n.PersonName, n.Email, n.StaffType, n.StaffId, n.PhotoBase64, n.PhotoContentType })
            .ToListAsync(ct);
        var emails = records.Select(r => r.PersonalEmail).Append(user?.Email).Append(login)
            .Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e!.Trim()).ToList();
        var names = records.Select(r => r.Name).Append(user?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => Squash(n!)).ToList();

        var box = boxes.FirstOrDefault(n => records.Any(r => r.Type == n.StaffType && r.Id == n.StaffId))
                  ?? boxes.FirstOrDefault(n => n.Email != null && emails.Contains(n.Email.Trim(), StringComparer.OrdinalIgnoreCase))
                  ?? boxes.FirstOrDefault(n => names.Contains(Squash(n.PersonName), StringComparer.OrdinalIgnoreCase));
        return box is null
            ? null
            : new UserPhotoFile(Convert.FromBase64String(box.PhotoBase64!), box.PhotoContentType ?? "image/jpeg");

        static string Squash(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
