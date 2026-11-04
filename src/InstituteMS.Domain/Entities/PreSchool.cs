namespace InstituteMS.Domain.Entities;

/*
 * Avijan PreSchool, run in the same database as the Computer Centre but in
 * tables of its own (all named PreSchool*). Nothing here is read by a Computer
 * Centre screen, and no Computer Centre table is written to by a PreSchool one:
 * the two schools share the sign-in and nothing else of these masters.
 *
 * There is no session or academic year. A child joins a class on any day and
 * moves up when they complete it, so every date belongs to the child, never to
 * a calendar.
 */

/// <summary>Maps to <c>dbo.PreSchoolClass</c> — Pre-Nursery, Nursery, KG-1, KG-2.</summary>
public class PreSchoolClass
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Order on every list and the order children move up in.</summary>
    public int SortOrder { get; set; }

    /// <summary>Charged once, when a child is admitted to this class.</summary>
    public decimal AdmissionFee { get; set; }

    /// <summary>Youngest and oldest age for the class in months, checked at admission. Null means no limit.</summary>
    public int? MinAgeMonths { get; set; }
    public int? MaxAgeMonths { get; set; }

    /// <summary>
    /// A class is retired rather than deleted: admissions, marks and receipts
    /// will point at it. A retired class is offered for nothing new.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public ICollection<PreSchoolSubject> Subjects { get; set; } = [];
    public ICollection<PreSchoolFeeHead> FeeHeads { get; set; } = [];
}

/// <summary>Maps to <c>dbo.PreSchoolSubject</c> — what a class is examined in, with full marks.</summary>
public class PreSchoolSubject
{
    public int Id { get; set; }
    public int ClassId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>The usual full marks; an exam can still set its own.</summary>
    public int MaxMarks { get; set; } = 100;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public PreSchoolClass? Class { get; set; }
}

/// <summary>
/// Maps to <c>dbo.PreSchoolFeeHead</c> — one monthly charge for a class
/// (tuition, books, activity). Every PreSchool fee is monthly; a child's month
/// is the sum of their class's active heads.
/// </summary>
public class PreSchoolFeeHead
{
    public int Id { get; set; }
    public int ClassId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal MonthlyAmount { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public PreSchoolClass? Class { get; set; }
}

/// <summary>
/// Maps to <c>dbo.PreSchoolGradeBand</c> — the grade for a percentage. A
/// percentage takes the band with the highest <see cref="MinPercent"/> it
/// reaches, so the lowest band should start at 0.
/// </summary>
public class PreSchoolGradeBand
{
    public int Id { get; set; }
    public decimal MinPercent { get; set; }
    public string Grade { get; set; } = string.Empty;

    /// <summary>Printed beside the grade on the report card, e.g. "Outstanding".</summary>
    public string? Remark { get; set; }

    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}

/// <summary>
/// Maps to <c>dbo.PreSchoolSetting</c> — one row (Id 1): the school's own
/// contact details for its home page and receipts, and the shop parents are
/// sent to for uniforms (Avijan does not sell them).
/// </summary>
public class PreSchoolSetting
{
    public int Id { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? MapUrl { get; set; }

    public string? UniformShopName { get; set; }
    public string? UniformShopAddress { get; set; }
    public string? UniformShopPhone { get; set; }
    public string? UniformShopMapUrl { get; set; }

    /// <summary>
    /// The day the Champahati centre opens. Until then (or while it is not set)
    /// both front pages say it is coming soon; from that day the tagline goes.
    /// </summary>
    public DateOnly? OpensOn { get; set; }

    /// <summary>Starts every PreSchool receipt number, e.g. APS/0001 — one running series.</summary>
    public string ReceiptPrefix { get; set; } = "APS";

    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}

/// <summary>
/// Maps to <c>dbo.PreSchoolEnquiry</c> — an admission enquiry sent from the
/// public PreSchool page. Kept apart from the Computer Centre's course
/// enquiries, so neither office sees the other's.
/// </summary>
public class PreSchoolEnquiry
{
    public int Id { get; set; }
    public string ChildName { get; set; } = string.Empty;
    public DateOnly? ChildDateOfBirth { get; set; }
    public string ParentName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }

    /// <summary>The class the parent asked about, as its name; null when they left it to the school.</summary>
    public string? ClassWanted { get; set; }

    public string? Message { get; set; }
    public string? ClientIp { get; set; }
    public DateTime CreatedOn { get; set; }

    /// <summary>Set when the office has called the parent back.</summary>
    public bool IsHandled { get; set; }
    public string? HandledBy { get; set; }
    public DateTime? HandledOn { get; set; }
}

/// <summary>
/// Maps to <c>dbo.PreSchoolHoliday</c> — a day or run of days the PreSchool is
/// closed, with a notice for parents. The PreSchool's own list: the Computer
/// Centre's holiday list is not read for it, since the two schools close on
/// different days.
/// </summary>
public class PreSchoolHoliday
{
    public int Id { get; set; }
    public DateOnly FromDate { get; set; }

    /// <summary>The last closed day; the same as <see cref="FromDate"/> for a single day.</summary>
    public DateOnly ToDate { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Note { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}

/// <summary>
/// Maps to <c>dbo.PreSchoolStudent</c> — one child admitted to the PreSchool.
/// Not a Computer Centre student: it never appears in dbo.Students, and the
/// child has no login of their own (the parent signs in instead).
/// </summary>
public class PreSchoolStudent
{
    public int Id { get; set; }

    /// <summary>One running number, shown as e.g. APS-0001.</summary>
    public int AdmissionNo { get; set; }

    public string ChildName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? BloodGroup { get; set; }

    public string? FatherName { get; set; }
    public string? MotherName { get; set; }

    /// <summary>The parent the school deals with, and whose phone it calls.</summary>
    public string ParentName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? EmergencyPhone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }

    public int ClassId { get; set; }

    /// <summary>Any day of the year; fees are due from this month on.</summary>
    public DateOnly JoinedOn { get; set; }

    /// <summary>Taken off every month's fee, e.g. for a second child.</summary>
    public decimal MonthlyDiscount { get; set; }

    /// <summary>Active, Completed (finished the class) or Left.</summary>
    public string Status { get; set; } = PreSchoolStudentStatus.Active;

    /// <summary>When the child completed the class or left; fees stop after this month.</summary>
    public DateOnly? EndedOn { get; set; }

    public string? Notes { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public PreSchoolClass? Class { get; set; }
    public ICollection<PreSchoolReceipt> Receipts { get; set; } = [];
}

public static class PreSchoolStudentStatus
{
    public const string Active = "Active";
    public const string Completed = "Completed";
    public const string Left = "Left";

    public static readonly string[] All = [Active, Completed, Left];
}

/// <summary>
/// Maps to <c>dbo.PreSchoolReceipt</c> — money received from a parent, with its
/// own running receipt number (APS/0001). What it paid for is in its lines.
/// </summary>
public class PreSchoolReceipt
{
    public int Id { get; set; }
    public int ReceiptNo { get; set; }
    public int StudentId { get; set; }
    public DateOnly ReceivedOn { get; set; }

    /// <summary>Cash, UPI or Paytm.</summary>
    public string Mode { get; set; } = "Cash";

    /// <summary>The UPI or Paytm transaction reference.</summary>
    public string? Reference { get; set; }

    public decimal Total { get; set; }
    public string? Notes { get; set; }

    /// <summary>Set when a receipt made in error is cancelled; its months are due again.</summary>
    public bool IsCancelled { get; set; }
    public string? CancelledBy { get; set; }
    public DateTime? CancelledOn { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }

    public PreSchoolStudent? Student { get; set; }
    public ICollection<PreSchoolReceiptLine> Lines { get; set; } = [];
}

/// <summary>
/// Maps to <c>dbo.PreSchoolReceiptLine</c> — one amount on a receipt: the
/// admission fee, or one fee head for one month. The amount is kept as charged,
/// so a later fee change never rewrites an old receipt.
/// </summary>
public class PreSchoolReceiptLine
{
    public int Id { get; set; }
    public int ReceiptId { get; set; }

    /// <summary>Admission or Monthly.</summary>
    public string Kind { get; set; } = "Monthly";

    /// <summary>The first of the month paid for; null for the admission fee.</summary>
    public DateOnly? ForMonth { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    public PreSchoolReceipt? Receipt { get; set; }
}
