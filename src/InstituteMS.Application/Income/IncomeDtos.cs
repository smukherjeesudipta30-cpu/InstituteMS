using InstituteMS.Application.Common;

namespace InstituteMS.Application.Income;

/// <summary>The sections of the income report, matching the legacy page's panels.</summary>
public enum IncomeSection
{
    /// <summary>Monthly fee collections (<c>MonthlyFees</c>).</summary>
    Fees = 0,
    /// <summary>Admission fees of students who joined in the range (<c>Student.StuAdFees</c>).</summary>
    Admissions = 1,
    /// <summary>Non-fee income (<c>OIncome</c>).</summary>
    OtherIncome = 2,
    /// <summary>Late fines (<c>LateFees</c>) - a breakdown of <see cref="Fees"/>, not extra income.</summary>
    LateFines = 3,
    /// <summary>Institute running costs (<c>Expense</c>).</summary>
    Expenses = 4,
    /// <summary>Teachers' share of fee collections (<c>TecFees</c>).</summary>
    TeacherPayouts = 5,
    /// <summary>Ad-hoc payments to teachers (<c>TecExpense</c>).</summary>
    TeacherExpenses = 6,
    /// <summary>Movement in student dues over the range (<c>NewDue minus OldDue</c>).</summary>
    Dues = 7,
    /// <summary>Receptionists' monthly pay (<c>ReceptionistPayment</c>).</summary>
    ReceptionistPay = 8,
    /// <summary>Admission referral commission credited to teachers and receptionists (<c>ReferralCredit</c>).</summary>
    ReferralCommission = 9
}

/// <summary>
/// Consolidated profit and loss for one date range.
///
/// <para>
/// <b>Late fines are not a separate income line.</b> <c>MonthlyFees.Installment</c>
/// is stored as installment + fine, so <see cref="LateFines"/> reports the portion
/// of <see cref="FeeCollections"/> that was fine. The legacy page listed both as
/// independent panels with independent totals, which invited double counting.
/// </para>
/// </summary>
public record IncomeReportDto(
    DateOnly From,
    DateOnly To,

    decimal FeeCollections,
    decimal AdmissionFees,
    decimal OtherIncome,
    decimal TotalIncome,

    /// <summary>Of which: the fine portion already inside <see cref="FeeCollections"/>.</summary>
    decimal LateFines,

    decimal Expenses,
    decimal TeacherPayouts,
    decimal TeacherExpenses,

    /// <summary>Receptionists’ monthly pay, a cost like any other salary.</summary>
    decimal ReceptionistPay,

    decimal TotalCost,

    decimal NetProfit,
    /// <summary>Net as a percentage of income; 0 when there is no income.</summary>
    decimal MarginPercent,

    /// <summary>Movement in outstanding dues across the range (may be negative).</summary>
    decimal DuesMovement,

    IReadOnlyList<IncomeSectionTotalDto> Sections,
    IReadOnlyList<IncomeTrendPointDto> Monthly,
    DateTime GeneratedOn,

    /// <summary>The branch this report is scoped to, or null for the whole institute.</summary>
    string? Branch = null,

    /// <summary>
    /// True when a branch filter is applied. Other income and teacher expenses are
    /// then excluded, because neither records a branch and neither can borrow one
    /// from a student â folding institute-wide figures into a single branch would
    /// understate that branch every time.
    /// </summary>
    bool BranchScoped = false);

public record IncomeSectionTotalDto(
    IncomeSection Section,
    string Label,
    decimal Amount,
    int RowCount,
    /// <summary>True when the amount counts against profit rather than towards it.</summary>
    bool IsCost,
    /// <summary>True when the amount is informational and excluded from the totals.</summary>
    bool IsBreakdown);

public record IncomeTrendPointDto(
    int Year,
    int Month,
    string Label,
    decimal Income,
    decimal Cost,
    decimal Net);

/// <summary>One detail row. Fields not relevant to a section are left null.</summary>
public record IncomeDetailDto(
    int Id,
    DateOnly? Date,
    string? Reference,
    string? StudentName,
    string? RegistrationId,
    string? CourseName,
    string? TeacherName,
    string? Description,
    decimal Amount);

public class IncomeDetailQuery : PagedQuery
{
    public IncomeSection Section { get; set; } = IncomeSection.Fees;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }

    /// <summary>Empty for the whole institute.</summary>
    public string? Branch { get; set; }
}

public interface IIncomeService
{
    Task<Result<IncomeReportDto>> GetReportAsync(
        DateOnly from, DateOnly to, string? branch = null, CancellationToken ct = default);

    Task<PagedResult<IncomeDetailDto>> GetDetailAsync(IncomeDetailQuery query, CancellationToken ct = default);
}
