namespace InstituteMS.Domain.Entities;

/// <summary>
/// A staff member's photograph — maps to <c>dbo.UserPhoto</c>, one row per user.
/// Held as Base64, the way student and org-chart photographs are.
/// </summary>
public class UserPhoto
{
    public int UserId { get; set; }
    public string ContentType { get; set; } = "image/jpeg";
    public string PhotoBase64 { get; set; } = string.Empty;
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
