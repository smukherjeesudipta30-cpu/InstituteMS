namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.LateFees</c> — fine charged on a late fee payment.</summary>
public class LateFee
{
    public int Id { get; set; }
    public string? StudentRegistrationId { get; set; }
    public string? BillNo { get; set; }
    public decimal? FineAmount { get; set; }
    public DateOnly? FineDate { get; set; }

    public string? CreatedBy { get; set; }
    /// <summary>
    /// Nullable: the audit columns were added to the legacy tables by the
    /// coexistence script and are NULL on every row that predates it - all 755
    /// users, all 953 students. A non-nullable property makes EF throw
    /// "Data is Null" on those rows. Unknown is the honest value; inventing a
    /// creation date would be worse.
    /// </summary>
    public DateTime? CreatedOn { get; set; }
}

/// <summary>
/// Maps to <c>dbo.TecFees</c> — the teacher's share of a student payment.
/// This is the institute's largest running cost, so the dashboard nets it off.
/// </summary>
public class TeacherFee
{
    public int Id { get; set; }
    public int? TeacherId { get; set; }
    public string? StudentRegistrationId { get; set; }
    public string? BillNo { get; set; }
    public decimal? Salary { get; set; }
    public DateOnly? PaidOn { get; set; }

    public string? CreatedBy { get; set; }
    /// <summary>
    /// Nullable: the audit columns were added to the legacy tables by the
    /// coexistence script and are NULL on every row that predates it - all 755
    /// users, all 953 students. A non-nullable property makes EF throw
    /// "Data is Null" on those rows. Unknown is the honest value; inventing a
    /// creation date would be worse.
    /// </summary>
    public DateTime? CreatedOn { get; set; }

    public Teacher? Teacher { get; set; }
}

/// <summary>Maps to <c>dbo.TecExpense</c> — ad-hoc payment to a teacher.</summary>
public class TeacherExpense
{
    public int Id { get; set; }
    public int? TeacherId { get; set; }
    public DateOnly? ExpenseDate { get; set; }
    public decimal? Amount { get; set; }

    public string? CreatedBy { get; set; }
    /// <summary>
    /// Nullable: the audit columns were added to the legacy tables by the
    /// coexistence script and are NULL on every row that predates it - all 755
    /// users, all 953 students. A non-nullable property makes EF throw
    /// "Data is Null" on those rows. Unknown is the honest value; inventing a
    /// creation date would be worse.
    /// </summary>
    public DateTime? CreatedOn { get; set; }

    public Teacher? Teacher { get; set; }
}

/// <summary>Maps to <c>dbo.Marks</c> — up to four subjects per exam.</summary>
public class Mark
{
    public int Id { get; set; }
    public string? StudentRegistrationId { get; set; }
    public DateOnly? ExamDate { get; set; }

    public string? Subject1 { get; set; }
    /// <summary>Full marks for the subject (legacy column M1).</summary>
    public int? FullMarks1 { get; set; }
    /// <summary>Marks obtained (legacy column OM1).</summary>
    public int? ObtainedMarks1 { get; set; }
    public string? Subject2 { get; set; }
    /// <summary>Full marks for the subject (legacy column M2).</summary>
    public int? FullMarks2 { get; set; }
    /// <summary>Marks obtained (legacy column OM2).</summary>
    public int? ObtainedMarks2 { get; set; }
    public string? Subject3 { get; set; }
    /// <summary>Full marks for the subject (legacy column M3).</summary>
    public int? FullMarks3 { get; set; }
    /// <summary>Marks obtained (legacy column OM3).</summary>
    public int? ObtainedMarks3 { get; set; }
    public string? Subject4 { get; set; }
    /// <summary>Full marks for the subject (legacy column M4).</summary>
    public int? FullMarks4 { get; set; }
    /// <summary>Marks obtained (legacy column OM4).</summary>
    public int? ObtainedMarks4 { get; set; }

    public string? CreatedBy { get; set; }
    /// <summary>
    /// Nullable: the audit columns were added to the legacy tables by the
    /// coexistence script and are NULL on every row that predates it - all 755
    /// users, all 953 students. A non-nullable property makes EF throw
    /// "Data is Null" on those rows. Unknown is the honest value; inventing a
    /// creation date would be worse.
    /// </summary>
    public DateTime? CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}

/// <summary>
/// Maps to <c>dbo.Fees</c> — a per-payment income roll-up the legacy income report
/// reads (Models/Income.cs E7). Written alongside every MonthlyFees row.
/// </summary>
public class FeeTotal
{
    public int Id { get; set; }
    public string? StudentRegistrationId { get; set; }
    public decimal? Total { get; set; }
    public DateOnly? StoreDate { get; set; }

    public string? CreatedBy { get; set; }
    /// <summary>
    /// Nullable: the audit columns were added to the legacy tables by the
    /// coexistence script and are NULL on every row that predates it - all 755
    /// users, all 953 students. A non-nullable property makes EF throw
    /// "Data is Null" on those rows. Unknown is the honest value; inventing a
    /// creation date would be worse.
    /// </summary>
    public DateTime? CreatedOn { get; set; }
}

/// <summary>Maps to <c>dbo.Branch</c>.</summary>
public class Branch
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Code { get; set; }
    public bool IsActive { get; set; }

    public string? CreatedBy { get; set; }
    /// <summary>
    /// Nullable: the audit columns were added to the legacy tables by the
    /// coexistence script and are NULL on every row that predates it - all 755
    /// users, all 953 students. A non-nullable property makes EF throw
    /// "Data is Null" on those rows. Unknown is the honest value; inventing a
    /// creation date would be worse.
    /// </summary>
    public DateTime? CreatedOn { get; set; }

    /* The legacy column set, kept in step with the one above.

       dbo.Branch carries both: Branch_name / Branch_id / Is_active from the
       legacy schema, and BranchName / BranchCode / IsActive added beside them
       so neither application has to change. Is_active is NOT NULL with no
       default, so a row written through the new names alone fails outright -
       and a row that did save would look blank to the legacy application. */
    public string? LegacyName { get; set; }
    public string? LegacyCode { get; set; }
    public bool LegacyIsActive { get; set; }
}
