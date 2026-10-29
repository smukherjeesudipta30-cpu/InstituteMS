namespace InstituteMS.Domain.Entities;

/// <summary>
/// The admission offer shown on the public front page — maps to
/// <c>dbo.SiteOffer</c>, a single row (Id 1). Shown from <see cref="StartsOn"/>
/// for <see cref="Days"/> days; set by Admin or Receptionist.
/// </summary>
public class SiteOffer
{
    public int Id { get; set; } = 1;
    public int Percentage { get; set; }
    public DateOnly StartsOn { get; set; }
    public int Days { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    /// <summary>The last day it shows: a 30-day offer starting the 1st ends on the 30th.</summary>
    public DateOnly EndsOn => StartsOn.AddDays(Days - 1);
}
