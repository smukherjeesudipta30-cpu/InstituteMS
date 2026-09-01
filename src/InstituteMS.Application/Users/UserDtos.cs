using InstituteMS.Application.Common;

namespace InstituteMS.Application.Users;

/// <summary>
/// A user as the list shows them. Never carries <c>PasswordHash</c> — the legacy
/// screen displayed the plain-text password in the grid (MIGRATION_PLAN §4.8 U1).
/// </summary>
public record UserListItemDto(
    int Id,
    string? Name,
    string? Email,
    string? Roles,
    IReadOnlyList<string> RoleList,
    string? Phone,
    DateOnly? DateOfBirth,
    DateOnly? RegisterDate,
    DateOnly? EndDate,
    bool IsActive,
    DateTime? LastLoginOn,
    bool HasPassword,

    /// <summary>
    /// For a student account only: the portal password as issued, read from
    /// <c>Student.Avijan_login_Password</c> where production keeps it in clear so
    /// the office can read it back. Null for staff - their password is a BCrypt
    /// hash and there is nothing to show.
    /// </summary>
    string? StudentPassword);

public record UserSaveRequest
{
    public string? Name { get; init; }
    public string? Email { get; init; }
    public string? Roles { get; init; }
    public string? Phone { get; init; }
    public DateOnly? DateOfBirth { get; init; }

    /// <summary>
    /// Required when creating. On update, leave null to keep the current password —
    /// supplying a value resets it and signs the user's other sessions out.
    /// </summary>
    public string? Password { get; init; }

    public bool IsActive { get; init; } = true;
}

/// <summary>Legacy <c>Birthday_wise_next30_days_Users</c> (U6).</summary>
public record UpcomingBirthdayDto(
    int Id,
    string? Name,
    string? Email,
    string? Phone,
    DateOnly? DateOfBirth,
    DateOnly NextBirthday,
    int DaysAway,
    int? TurningAge);

public class UserQuery : PagedQuery
{
    /// <summary>null = both. Defaults to showing everyone, as the legacy grid did.</summary>
    public bool? IsActive { get; set; }

    /// <summary>Legacy "Search User by Email" box — a LIKE match.</summary>
    public string? Email { get; set; }

    public string? Role { get; set; }
}

/// <summary>
/// Sets one user's password to a value the administrator has chosen or generated.
/// </summary>
/// <remarks>
/// This exists because passwords cannot be read back. The legacy Users grid showed
/// the plain-text <c>Password</c> column so an administrator could tell staff what
/// their sign-in was; those passwords are BCrypt hashes now, so the equivalent is to
/// <em>set</em> a known one and hand that over.
/// </remarks>
public record SetPasswordRequest
{
    public string Password { get; init; } = string.Empty;
}
