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
