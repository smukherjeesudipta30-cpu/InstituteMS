namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.Teacher</c>.</summary>
public class Teacher
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? LoginId { get; set; }
    public string? Phone { get; set; }

    /// <summary>Real mailbox for attendance codes; the LoginId is not one.</summary>
    public string? PersonalEmail { get; set; }

    /* Shown on the staff hierarchy chart. All free text the administrator types --
       the owner asked that designation be dynamic, so there is no fixed list of
       positions to choose from and no assumption about how many people hold any
       of them. */
    public string? Designation { get; set; }
    public string? Qualification { get; set; }
    public string? Branch { get; set; }

    /// <summary>
    /// A teacher who has left. Kept rather than deleted, so their attendance and
    /// salary history survives; dropped from the chart.
    /// </summary>
    public bool IsActive { get; set; } = true;

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

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
