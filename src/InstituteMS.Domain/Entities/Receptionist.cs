namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.Receptionist</c> — one row per receptionist.</summary>
/// <remarks>
/// Receptionists existed only as sign-ins before this: seven users carried the
/// role with no master record, so there was nowhere to say who they are, which
/// branch they work at, or what they have been paid.
/// </remarks>
public class Receptionist
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// How they sign in, matching <c>dbo.Users.Email</c>. Null when someone is on
    /// the payroll before an account has been created for them.
    /// </summary>
    public string? LoginId { get; set; }

    public string? Phone { get; set; }

    /// <summary>Real mailbox for attendance codes; the LoginId is not one.</summary>
    public string? PersonalEmail { get; set; }

    /* Shown on the staff hierarchy chart -- free text, as for a teacher. */
    public string? Designation { get; set; }
    public string? Qualification { get; set; }

    /// <summary>What computer training this person holds.</summary>
    /// <remarks>
    /// Kept apart from <see cref="Qualification"/> deliberately: an institute that
    /// teaches computing has reason to record what somebody studied and what computer
    /// training they hold as two different answers. Required of everyone except
    /// technical staff -- see <see cref="IsTechnical"/>.
    /// </remarks>
    public string? ComputerTraining { get; set; }

    /// <summary>
    /// Technical staff, who are excused the qualification and computer-training
    /// requirement. A tick the administrator sets, not a job title this code
    /// recognises -- "IT Support" and "Systems" are the same role by another name.
    /// </summary>
    public bool IsTechnical { get; set; }

    /// <summary>
    /// Which branch they work at. Free text matching <c>Student.Branch</c> and
    /// <c>Expense.Branch</c>, so all three group together in a branch report.
    /// </summary>
    public string? Branch { get; set; }

    /// <summary>


    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public ICollection<ReceptionistPayment> Payments { get; set; } = [];
}

/// <summary>
/// Maps to <c>dbo.ReceptionistPayment</c> — one row per month paid.
/// </summary>
public class ReceptionistPayment
{
    public int Id { get; set; }
    public int ReceptionistId { get; set; }

    /// <summary>
    /// The month this payment is for, stored as the first of that month so
    /// month-wise grouping needs no date arithmetic.
    /// </summary>
    public DateOnly PayMonth { get; set; }

    /// <summary>When it was actually handed over, which may fall in another month.</summary>
    public DateOnly? PaidOn { get; set; }

    public decimal Amount { get; set; }

    /// <summary>
    /// Copied from the receptionist when the payment is recorded, so a branch
    /// report stays correct if they later move between branches.
    /// </summary>
    public string? Branch { get; set; }

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }

    public Receptionist? Receptionist { get; set; }
}

/// <summary>
/// Maps to <c>dbo.ReceptionistLeaveDeduction</c> — one row per receptionist per
/// reporting period (week/month/year) once an Admin has looked at that period's
/// leave and decided whether to dock pay for it.
/// </summary>
/// <remarks>
/// Leave itself is never stored — same rule as attendance/absence in
/// <see cref="StaffAttendance"/>: for a given period it is computed as rostered
/// days (from <see cref="StaffShiftAssignment"/>) minus scanned-present days. Only
/// the Admin's *decision* about that computed leave — the percentage to cut, and
/// whether it was actually applied — needs a durable row, since a report re-run
/// later must not silently forget what was already decided.
/// </remarks>
public class ReceptionistLeaveDeduction
{
    public int Id { get; set; }
    public int ReceptionistId { get; set; }

    /// <summary><c>Weekly</c>, <c>Monthly</c> or <c>Yearly</c> — matches the report period this decision was made against.</summary>
    public string PeriodType { get; set; } = "Monthly";

    /// <summary>First day of the period (the Monday of the week, the 1st of the month, or Jan 1 of the year).</summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>How many days' leave this person was entitled to for the period, at the time the decision was made.</summary>
    public decimal EntitlementDays { get; set; }

    /// <summary>How many days' leave they actually took, at the time the decision was made.</summary>
    public decimal TakenDays { get; set; }

    /// <summary>What percentage of salary the Admin has chosen to deduct. Zero unless a deduction was actually set.</summary>
    public decimal DeductionPercent { get; set; }

    /// <summary>
    /// The Admin's actual decision, separate from the percentage: a percentage can
    /// be entered and left un-applied while still deciding, or applied and later
    /// reversed without losing the number that was typed in.
    /// </summary>
    public bool Applied { get; set; }

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public Receptionist? Receptionist { get; set; }
}
