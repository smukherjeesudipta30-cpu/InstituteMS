namespace InstituteMS.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.StaffShiftAssignment</c> — one line of the rota: this person works
/// this shift on this weekday.
/// </summary>
/// <remarks>
/// More than one row per person per day is allowed on purpose. Ishani works both the
/// morning and the evening on a Sunday, and a single shift column on the staff record
/// could not say so.
/// </remarks>
public class StaffShiftAssignment
{
    public int Id { get; set; }

    /// <summary><c>Teacher</c> or <c>Receptionist</c>.</summary>
    public string StaffType { get; set; } = string.Empty;

    public int StaffId { get; set; }

    /// <summary>0 = Sunday … 6 = Saturday, matching <see cref="System.DayOfWeek"/>.</summary>
    public byte DayOfWeek { get; set; }

    public int ShiftId { get; set; }

    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public AttendanceShift? Shift { get; set; }
}
