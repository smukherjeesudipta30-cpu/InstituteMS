namespace InstituteMS.Domain.Entities;

/// <summary>
/// One global row, maps to <c>dbo.PayrollSettings</c>.
/// </summary>
/// <remarks>
/// Read fresh on every report rather than cached in configuration, so the
/// admin's own toggle takes effect immediately -- no deploy, no engineer.
/// </remarks>
public class PayrollSettings
{
    public int Id { get; set; }

    /// <summary>
    /// Whether the Teacher Account report deducts for missed scan-outs at all.
    /// Off means every teacher's report reads exactly as it did before this
    /// feature existed, regardless of how many scan-outs anyone missed.
    /// </summary>
    public bool MissedScanOutDeductionEnabled { get; set; }

    /// <summary>
    /// The day of the month a receptionist's pay cycle starts on — 26 by
    /// default, so "this month" for pay purposes runs the 26th to the 25th of
    /// the next, not the 1st to the end. Everything keyed to a pay month (leave
    /// entitlement, the extra-shift bonus) resolves its date range from this
    /// rather than the calendar month, so an Admin can move the whole
    /// institute's cycle without a deploy.
    /// </summary>
    public int PayCycleStartDay { get; set; } = 26;

    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
