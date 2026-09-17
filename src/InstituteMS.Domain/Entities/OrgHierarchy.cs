namespace InstituteMS.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.OrgNode</c> — one box on the staff hierarchy chart.
/// </summary>
/// <remarks>
/// <para>A box keeps its own name, position, qualification, email, photograph,
/// branch and active flag. <see cref="StaffId"/> points at a Teacher or
/// Receptionist record only as a reference, never as the source of those values:
/// the owner asked that every name, the image and the mail id stay editable on the
/// chart, and a box such as "Technical Support" is a function with nobody attached
/// to it at all.</para>
///
/// <para>That separation also means editing the chart cannot disturb the staff
/// master, and renaming a teacher cannot silently rewrite the chart.</para>
/// </remarks>
public class OrgNode
{
    public int Id { get; set; }

    /// <summary>Editable. Not tied to the staff record's name.</summary>
    public string PersonName { get; set; } = string.Empty;

    /// <summary>
    /// The position, as the administrator types it — "Owner", "Head Receptionist
    /// &amp; HR", "Entertainment &amp; Play Section".
    /// </summary>
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

    /// <summary>May differ from the address on the staff record, by design.</summary>
    public string? Email { get; set; }

    public string? Branch { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Left-to-right placement among boxes sharing a parent.</summary>
    public int SortOrder { get; set; }

    /// <summary><c>Teacher</c>, <c>Receptionist</c>, or null for a function.</summary>
    public string? StaffType { get; set; }
    public int? StaffId { get; set; }

    /// <summary>
    /// When true, every teacher and receptionist who has no box of their own is drawn
    /// beneath this one.
    /// </summary>
    /// <remarks>
    /// The owner asked that a teacher added next year appear on the chart too. A list
    /// of thirty-six hand-made boxes could not do that without somebody remembering to
    /// extend it, so membership is a rule instead: hire a teacher, and they show up
    /// under whichever box carries this flag (today, Nargis Khatun). Exactly one box
    /// may carry it — a filtered unique index enforces that.
    /// </remarks>
    public bool IsDefaultStaffParent { get; set; }

    /* Held inline as Base64, the way student photos are, so a photograph travels
       with the database backup rather than living on a file share that has to be
       backed up separately and restored in step. */
    public string? PhotoFileName { get; set; }
    public string? PhotoContentType { get; set; }
    public string? PhotoBase64 { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}

/// <summary>
/// Maps to <c>dbo.OrgEdge</c> — one reporting line, from a box to the box below it.
/// </summary>
/// <remarks>
/// Separate from <see cref="OrgNode"/> because the hierarchy is not a tree.
/// Technical Support reports to both Nargis and Arkita, and a "ManagerId" column on
/// the node could hold only one of them.
/// </remarks>
public class OrgEdge
{
    public int Id { get; set; }

    public int ParentId { get; set; }
    public int ChildId { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }

    public OrgNode? Parent { get; set; }
    public OrgNode? Child { get; set; }
}
