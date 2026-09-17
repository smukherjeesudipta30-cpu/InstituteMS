namespace InstituteMS.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.AttendanceShift</c> — a sitting at the front desk, and the hour by
/// which whoever worked it must have scanned out.
/// </summary>
/// <remarks>
/// Two exist: Morning (10:00–13:00, out by 15:00) and Evening (17:00–21:00, out by
/// 23:00). The deadline is held rather than computed from <see cref="EndTime"/>,
/// because the grace after each shift is the owner's decision and not a formula —
/// lengthening one should not silently lengthen the other.
/// </remarks>
public class AttendanceShift
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>
    /// Scan out after this and the day counts as leave. Applies to receptionists;
    /// teachers have no shift and fall back to the flat eight-hour rule.
    /// </summary>
    public TimeOnly ScanOutBy { get; set; }

    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
