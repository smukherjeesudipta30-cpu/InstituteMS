using InstituteMS.Application.Common;
using InstituteMS.Application.Fees;

namespace InstituteMS.Application.Reports;

/* ============================================================== admissions */

/// <summary>
/// Filters for the new-admissions report. Replaces the two date pickers on
/// <c>Views/StudentReport/MonthlyIndex.cshtml</c>.
/// </summary>
public class AdmissionsReportQuery : PagedQuery
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }

    /// <summary>Optional course filter. The legacy page had none - it listed every course.</summary>
    public int? CourseId { get; set; }

    /// <summary>Optional branch filter. Empty for every branch.</summary>
    public string? Branch { get; set; }
}

public record AdmissionRowDto(
    int StudentId,
    string? RegistrationId,
    string? StudentName,
    string? Phone,
    DateOnly? DateOfJoining,
    string? CourseName,
    decimal AdmissionFees);

/// <summary>Rows plus the totals the legacy page left the reader to add up by eye.</summary>
public record AdmissionsReportDto(
    DateOnly From,
    DateOnly To,
    int StudentCount,
    decimal TotalAdmissionFees,
    PagedResult<AdmissionRowDto> Rows,
    DateTime GeneratedOn);

/* ==================================================================== dues */

/// <summary>Filters for the unpaid-students report (legacy <c>usp_NotPaidData</c>).</summary>
public class DuesReportQuery : PagedQuery
{
    /// <summary>Course to report on. Required - the legacy procedure took it as a parameter.</summary>
    public int CourseId { get; set; }

    public DateOnly From { get; set; }
    public DateOnly To { get; set; }

    /// <summary>
    /// Include active students who have never paid anything at all. The legacy
    /// procedure's inner join dropped them, hiding the students most in arrears.
    /// Defaults to true; set false to reproduce the legacy row set exactly.
    /// </summary>
    public bool IncludeNeverPaid { get; set; } = true;

    /// <summary>Optional branch filter. Empty for every branch.</summary>
    public string? Branch { get; set; }
}

public record DuesRowDto(
    int StudentId,
    string? RegistrationId,
    string? StudentName,
    string? Phone,
    DateOnly? DateOfJoining,
    string? CourseName,
    decimal AdmissionFees,
    /// <summary>Most recent payment of any date, or null if the student has never paid.</summary>
    DateOnly? LastPaymentDate,

    /// <summary>
    /// What the student owes as at the report's <c>To</c> date: the balance left on
    /// their last payment, plus every fee month that has passed since without one.
    /// </summary>
    /// <remarks>
    /// This used to be the carried balance alone, which is nearly always zero — a
    /// student who settles each bill in full leaves nothing carried — so a report
    /// listing people who had not paid for months showed ₹0.00 against every one of
    /// them. The carried balance is still reported, as <see cref="CarriedDue"/>.
    /// </remarks>
    decimal OutstandingDue,

    /// <summary>The balance left unpaid at the time of the last payment.</summary>
    decimal CarriedDue,

    /// <summary>
    /// Complete fee months elapsed since the last payment (or since joining, for a
    /// student who has never paid). A fee month is 32 days, the same figure the late
    /// fine uses, so the two agree about when a month has gone by.
    /// </summary>
    int MonthsUnpaid,

    /// <summary>The student's agreed monthly fee, which is what a missed month costs.</summary>
    decimal MonthlyFee,

    /// <summary>
    /// The fine that would apply if they paid on the report's <c>To</c> date, from
    /// the same <c>LateFineCalculator</c> the fee counter uses — so the figure here
    /// and the figure charged at the desk cannot disagree.
    /// </summary>
    decimal LateFine,

    /// <summary>False when the student has no payment history at all.</summary>
    bool HasEverPaid);

public record DuesReportDto(
    DateOnly From,
    DateOnly To,
    int CourseId,
    string? CourseName,
    int StudentCount,
    int NeverPaidCount,
    decimal TotalOutstanding,
    PagedResult<DuesRowDto> Rows,
    DateTime GeneratedOn);

/* ================================================================ expenses */

public class ExpenseReportQuery : PagedQuery
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }

    /// <summary>Optional branch filter. Empty for every branch.</summary>
    public string? Branch { get; set; }
}

public record ExpenseRowDto(
    int ExpenseId,
    string? Details,
    DateOnly? ExpenseDate,
    decimal Amount,
    string? Branch);

public record ExpenseReportDto(
    DateOnly From,
    DateOnly To,
    int EntryCount,
    decimal TotalAmount,
    PagedResult<ExpenseRowDto> Rows,
    DateTime GeneratedOn);

/* ======================================================== teacher earnings */

public record TeacherEarningRowDto(
    string? StudentName,
    string? RegistrationId,
    string? CourseName,
    DateOnly? PaidOn,
    decimal Salary);

public record TeacherPaymentRowDto(
    int ExpenseId,
    DateOnly? PaidOn,
    decimal Amount);

/// <summary>
/// One teacher's account for a date range: what they earned from student fees,
/// what has actually been handed over, and the balance.
/// </summary>
/// <remarks>
/// The legacy page computed the three totals in the browser with <c>Math.abs()</c>
/// on every row (Scripts/TeacherReport.js), so a corrected negative entry was added
/// as though it were a payment. They are summed in SQL here.
/// </remarks>
public record TeacherEarningsReportDto(
    int TeacherId,
    string? TeacherName,
    string? TeacherPhone,
    DateOnly From,
    DateOnly To,

    /// <summary>Teacher's share of fees collected in the range (<c>TecFees</c>).</summary>
    decimal TotalEarned,
    /// <summary>Amounts actually paid to the teacher in the range (<c>TecExpense</c>).</summary>
    decimal TotalPaid,
    /// <summary>Earned minus paid. Negative means the teacher has been overpaid.</summary>
    decimal Pending,

    IReadOnlyList<TeacherEarningRowDto> Earnings,
    IReadOnlyList<TeacherPaymentRowDto> Payments,
    DateTime GeneratedOn);

/* ===================================================== students by teacher */

public class StudentsByTeacherQuery : PagedQuery
{
    public int TeacherId { get; set; }

    /// <summary>
    /// Restrict to currently active students, as the legacy query did with
    /// <c>s.IsActive = 1</c>. Set false to include those who have left.
    /// </summary>
    public bool ActiveOnly { get; set; } = true;
}

public record StudentByTeacherRowDto(
    int StudentId,
    string? RegistrationId,
    string? StudentName,
    string? GuardianName,
    string? Address,
    string? Phone,
    string? Email,
    DateOnly? DateOfBirth,
    DateOnly? DateOfJoining,
    string? CourseName,
    int? CourseDuration,
    decimal MonthlyFees,
    int? TeacherCount,
    /// <summary>Which of the student's three teacher slots the filtered teacher sits in (1-3).</summary>
    int MatchedSlot,
    string? Teacher1Name,
    string? Teacher1Phone,
    string? Teacher2Name,
    string? Teacher2Phone,
    string? Teacher3Name,
    string? Teacher3Phone,
    bool IsActive,
    bool IsComplete);

public record StudentsByTeacherReportDto(
    int TeacherId,
    string? TeacherName,
    int StudentCount,
    PagedResult<StudentByTeacherRowDto> Rows,
    DateTime GeneratedOn);

/* ======================================================= student statement */

public record StatementPaymentDto(
    int FeeId,
    string? BillNo,
    DateOnly? PaymentDate,
    /// <summary>What was taken at the counter, fine included.</summary>
    decimal AmountPaid,
    /// <summary>The instalment portion, excluding any fine.</summary>
    decimal InstalmentOnly,
    decimal Fine,
    decimal DueAfter,
    string? CourseName);

public record StatementSubjectDto(string? Subject, int FullMarks, int ObtainedMarks);

public record StatementMarkDto(
    int MarkId,
    DateOnly? ExamDate,
    IReadOnlyList<StatementSubjectDto> Subjects,
    int TotalFullMarks,
    int TotalObtainedMarks,
    decimal PercentScored,
    /// <summary>Grade band from the legacy certificate rule.</summary>
    string Grade);

/// <summary>
/// A single student's own statement - the page a signed-in student sees.
/// Replaces <c>Views/StudentReport/Index.cshtml</c>.
/// </summary>
public record StudentStatementDto(
    int StudentId,
    string? RegistrationId,
    string? StudentName,
    string? GuardianName,
    string? Phone,
    string? Email,
    DateOnly? DateOfJoining,
    DateOnly? DateOfExit,
    string? CourseName,
    int? CourseDuration,
    decimal AdmissionFees,
    decimal AgreedMonthlyFees,
    bool IsActive,
    bool IsComplete,
    bool HasPhoto,

    int PaymentCount,
    decimal TotalPaid,
    decimal TotalFines,
    /// <summary>Balance carried on the most recent payment.</summary>
    decimal CurrentDue,
    /// <summary>Months paid for, the same count the course-progress report uses.</summary>
    int MonthsPaid,

    IReadOnlyList<StatementPaymentDto> Payments,
    IReadOnlyList<StatementMarkDto> Marks,
    DateTime GeneratedOn);

/* =============================================================== certificate */

/// <summary>
/// The course-completion certificate a student downloads from their statement.
/// Replaces <c>Home/getPrintCertificate</c> + <c>Views/Home/PrintCertificate.cshtml</c>.
/// </summary>
public record StudentCertificateDto(
    string? StudentName,
    string? GuardianName,
    string? RegistrationId,
    string? CourseName,
    int? CourseDuration,
    DateOnly? DateOfJoining,
    /// <summary>Date of the exam the grade is taken from.</summary>
    DateOnly? ExamDate,

    int TotalFullMarks,
    int TotalObtainedMarks,
    decimal PercentScored,
    string Grade,

    /// <summary>
    /// False when the student is still enrolled. The legacy page issued the
    /// certificate regardless, so the UI warns rather than refuses.
    /// </summary>
    bool IsComplete,
    DateTime GeneratedOn,

    /// <summary>
    /// Which copy this is. 1 is the original; 2 and above print as duplicates.
    /// </summary>
    int IssueNo,

    /// <summary>
    /// True from the second copy onwards. The printed certificate then carries a
    /// DUPLICATE COPY mark and its copy number, so a reissue can never be mistaken
    /// for the original.
    /// </summary>
    bool IsDuplicate,

    /// <summary>When the original was taken. Null on the original itself.</summary>
    DateTime? OriginalIssuedOn);

/* ================================================================= service */

public interface IReportService
{
    Task<Result<AdmissionsReportDto>> GetAdmissionsAsync(AdmissionsReportQuery query, CancellationToken ct = default);

    Task<Result<DuesReportDto>> GetDuesAsync(DuesReportQuery query, CancellationToken ct = default);

    Task<Result<ExpenseReportDto>> GetExpensesAsync(ExpenseReportQuery query, CancellationToken ct = default);

    Task<Result<TeacherEarningsReportDto>> GetTeacherEarningsAsync(
        int teacherId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<Result<StudentsByTeacherReportDto>> GetStudentsByTeacherAsync(
        StudentsByTeacherQuery query, CancellationToken ct = default);

    Task<Result<StudentStatementDto>> GetStudentStatementAsync(
        string registrationId, CancellationToken ct = default);

    /// <summary>
    /// Resolves the registration id of the student signed in with this email, the
    /// way the legacy page did from <c>Session["UserID"]</c>.
    /// </summary>
    Task<Result<string>> ResolveOwnRegistrationIdAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Resolves the teacher record belonging to this signed-in email, the way the
    /// legacy <c>Teacher.GetTeachers</c> matched <c>Teacher.LoginId</c> against
    /// <c>Session["UserID"]</c>. Lets a Teacher open their own account and no other.
    /// </summary>
    Task<Result<int>> ResolveOwnTeacherIdAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Issues the signed-in student's completion certificate, recording the copy.
    /// Replaces the <em>Download Certificate</em> button on the legacy student
    /// report.
    /// </summary>
    /// <remarks>
    /// This <b>records an issue</b> and is therefore not a read: the first call
    /// returns the original, and every call after it returns the next numbered
    /// duplicate. There is deliberately no non-recording variant — a second
    /// endpoint returning the same document without counting it would be the
    /// obvious way to take an unmarked copy.
    /// </remarks>
    /// <summary>
    /// Admissions, fee payments and expenses by branch, bucketed by month,
    /// quarter or year. Records with no branch report as <c>Unassigned</c> rather
    /// than being dropped, so the totals reconcile against the other reports.
    /// </summary>
    Task<Result<BranchReportDto>> GetBranchReportAsync(
        BranchReportQuery query, CancellationToken ct = default);

    Task<Result<StudentCertificateDto>> IssueOwnCertificateAsync(
        string email, CancellationToken ct = default);

    /// <summary>
    /// The money receipt for one of the signed-in student's own payments — the
    /// per-row <em>Download</em> button on the legacy student report. Returns
    /// <see cref="ResultStatus.Forbidden"/> for a payment belonging to anyone else.
    /// </summary>
    Task<Result<FeeReceiptDto>> GetOwnReceiptAsync(
        string email, int feeId, CancellationToken ct = default);
}

/* ========================================================= branch report */

/// <summary>How a branch report's rows are bucketed in time.</summary>
public enum BranchReportGrouping
{
    Month = 0,
    Quarter = 1,
    Year = 2
}

public class BranchReportQuery
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }

    public BranchReportGrouping GroupBy { get; set; } = BranchReportGrouping.Month;

    /// <summary>Empty for every branch. <c>Unassigned</c> selects records with no branch.</summary>
    public string? Branch { get; set; }
}

/// <summary>
/// One branch in one period: what came in, what went out, and the difference.
/// </summary>
public record BranchPeriodRowDto(
    string Branch,

    /// <summary>First day of the bucket — the sort key.</summary>
    DateOnly PeriodStart,
    /// <summary>Human label: <c>Aug 2026</c>, <c>Q3 2026</c> or <c>2026</c>.</summary>
    string PeriodLabel,

    /// <summary>Students who joined in this period.</summary>
    int Admissions,
    decimal AdmissionFees,

    /// <summary>Monthly fee payments taken in this period.</summary>
    int Payments,
    decimal FeesCollected,
    decimal LateFines,

    decimal Expenses,

    /// <summary>Non-fee income earned by this branch in the period.</summary>
    decimal OtherIncome,

    /// <summary>Receptionist salary paid by this branch in the period.</summary>
    decimal ReceptionistPay,

    /// <summary>
    /// (admission fees + fees collected + late fines + other income)
    /// − (expenses + receptionist pay).
    ///
    /// Teacher payouts are NOT subtracted: they are recorded against a teacher,
    /// not a branch, and a teacher may teach at more than one. Attributing them
    /// would be inventing a number, so they are excluded and said to be excluded.
    /// </summary>
    decimal Net);

/// <summary>A branch's whole-range total, across every period.</summary>
public record BranchTotalDto(
    string Branch,
    int Admissions,
    decimal AdmissionFees,
    int Payments,
    decimal FeesCollected,
    decimal LateFines,
    decimal Expenses,
    decimal OtherIncome,
    decimal ReceptionistPay,
    decimal Net);

/// <summary>
/// Admissions, fee payments and expenses by branch, bucketed by month, quarter
/// or year.
/// </summary>
/// <remarks>
/// A record reaches a branch differently depending on what it is: an admission
/// and a payment through the student's own branch; an expense, other income and
/// receptionist pay through the branch recorded on the record itself. Anything
/// with no branch is reported as <c>Unassigned</c> rather than dropped, so the
/// totals still reconcile against the unfiltered reports.
///
/// <para>Teacher payouts are the one cost deliberately absent: dbo.TecExpense is
/// keyed to a teacher, and a teacher may teach students at several branches, so
/// there is no truthful way to split their pay between branches.</para>
/// </remarks>
public record BranchReportDto(
    DateOnly From,
    DateOnly To,
    string GroupBy,

    IReadOnlyList<BranchPeriodRowDto> Rows,
    IReadOnlyList<BranchTotalDto> BranchTotals,
    BranchTotalDto GrandTotal,

    /// <summary>Every branch name seen, for the filter.</summary>
    IReadOnlyList<string> Branches,

    /// <summary>
    /// True while most records carry no branch, so the page can say the report is
    /// only as good as the branch data behind it rather than implying the
    /// institute really does have one branch.
    /// </summary>
    bool MostlyUnassigned,

    DateTime GeneratedOn);
