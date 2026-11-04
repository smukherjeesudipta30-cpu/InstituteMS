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

    /// <summary>Starts every PreSchool receipt number, e.g. APS/0001 — one running series.</summary>
    public string ReceiptPrefix { get; set; } = "APS";

    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
