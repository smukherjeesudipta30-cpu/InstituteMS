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

    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
