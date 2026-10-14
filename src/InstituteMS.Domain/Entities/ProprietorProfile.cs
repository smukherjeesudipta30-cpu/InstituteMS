namespace InstituteMS.Domain.Entities;

/// <summary>
/// The Proprietor's name, date of birth and phone, for the birthday alert — maps
/// to <c>dbo.ProprietorProfile</c>, a single row (Id 1). The Proprietor has no
/// login account, so this is the only place their birthday is kept.
/// </summary>
public class ProprietorProfile
{
    public int Id { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? Phone { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
