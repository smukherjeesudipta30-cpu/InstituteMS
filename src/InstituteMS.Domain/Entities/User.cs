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
    public string? RefreshToken { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEnd { get; set; }
    public DateTime? LastLoginOn { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public bool IsLockedOut(DateTime utcNow) => LockoutEnd.HasValue && LockoutEnd.Value > utcNow;
}
