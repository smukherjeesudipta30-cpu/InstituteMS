namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.Users</c>. Passwords are BCrypt hashes only — the legacy plain-text column is gone.</summary>
public class User
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Roles { get; set; }
    public string? LoginId { get; set; }
    public string? PhoneNumber { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? RegisterDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; }

    public string? PasswordHash { get; set; }

    /// <summary>
    /// Set on the 750 accounts migrated from the legacy system, whose password was
    /// hashed from the plain text the old application stored. They sign in once with
    /// the password they already knew and must then choose a new one: until they do,
    /// the access token carries <c>must_change_password</c> and every endpoint except
    /// the change-password flow itself refuses the request.
    /// </summary>
    public bool MustChangePassword { get; set; }

    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public DateTime? LastLoginOn { get; set; }

    /// <summary>
    /// When this person's chat connection was last live — updated as they
    /// connect and again as they disconnect, so an offline contact can be shown
    /// as "last seen …". Distinct from <see cref="LastLoginOn"/>, which is the
    /// sign-in event, not chat activity.
    /// </summary>
    public DateTime? LastSeenOn { get; set; }

    /// <summary>
    /// Hashed the same way as <see cref="PasswordHash"/>, not stored in clear — the
    /// six-digit code emailed by "forgot password". Cleared the moment it is spent
    /// or replaced by a fresh request.
    /// </summary>
    public string? PasswordResetCodeHash { get; set; }
    public DateTime? PasswordResetCodeExpiry { get; set; }

    /// <summary>
    /// Wrong-code attempts against the current code. Locked out the same way a
    /// wrong-password run locks the account, so guessing a six-digit code by brute
    /// force is not a shortcut around the password itself.
    /// </summary>
    public int PasswordResetAttempts { get; set; }

    public string? CreatedBy { get; set; }
    /// <summary>
    /// Nullable: the audit columns were added to the legacy tables by the
    /// coexistence script and are NULL on every row that predates it - all 755
    /// users, all 953 students. A non-nullable property makes EF throw
    /// "Data is Null" on those rows. Unknown is the honest value; inventing a
    /// creation date would be worse.
    /// </summary>
    public DateTime? CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public bool IsLockedOut(DateTime utcNow) => LockoutEnd.HasValue && LockoutEnd.Value > utcNow;
}
