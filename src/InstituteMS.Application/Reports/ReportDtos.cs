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
    /// <summary>Outstanding balance carried on that last payment.</summary>
    decimal OutstandingDue,
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
}

public record ExpenseRowDto(
    int ExpenseId,
    string? Details,
    DateOnly? ExpenseDate,
    decimal Amount);

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
    DateTime GeneratedOn);

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
    /// The signed-in student's completion certificate. Replaces the
    /// <em>Download Certificate</em> button on the legacy student report.
    /// </summary>
    Task<Result<StudentCertificateDto>> GetOwnCertificateAsync(
        string email, CancellationToken ct = default);

    /// <summary>
    /// The money receipt for one of the signed-in student's own payments — the
    /// per-row <em>Download</em> button on the legacy student report. Returns
    /// <see cref="ResultStatus.Forbidden"/> for a payment belonging to anyone else.
    /// </summary>
    Task<Result<FeeReceiptDto>> GetOwnReceiptAsync(
        string email, int feeId, CancellationToken ct = default);
}
