namespace InstituteMS.Domain.Entities;

/// <summary>
/// One day on the holiday list. The West Bengal government list is loaded as
/// a starting point, but the institute does not close on every one of those
/// days — <see cref="IsInstituteHoliday"/> is what Admin sets to say whether
/// the institute is actually closed, and Admin can add the institute's own days.
/// </summary>
public class Holiday
{
    public int Id { get; set; }
    public DateOnly HolidayDate { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// "National", "State" (the West Bengal government list), "Festival" (fetched
    /// from the Indian holiday calendar) or "Institute" (added by Admin).
    /// </summary>
    public string Category { get; set; } = HolidayCategories.Institute;

    /// <summary>True when the institute is closed that day; false when it stays open despite the government holiday.</summary>
    public bool IsInstituteHoliday { get; set; } = true;

    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}

public static class HolidayCategories
{
    public const string National = "National";
    public const string State = "State";
    public const string Festival = "Festival";
    public const string Institute = "Institute";

    public static readonly string[] All = [National, State, Festival, Institute];
}

/// <summary>
/// Marks a year whose list has already been loaded (from the West Bengal
/// notification or the calendar feed), so opening that year again never
/// re-fetches — even after Admin has deleted days from it on purpose.
/// </summary>
public class HolidayYearImport
{
    public int Year { get; set; }
    public string Source { get; set; } = string.Empty;
    public int Added { get; set; }
    public DateTime ImportedOn { get; set; }
    public string? ImportedBy { get; set; }
}
