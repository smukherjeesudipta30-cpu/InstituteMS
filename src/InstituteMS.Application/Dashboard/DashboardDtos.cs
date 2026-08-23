namespace InstituteMS.Application.Dashboard;

/// <summary>Everything the Home Dashboard needs, in one round trip (mobile-friendly).</summary>
public record DashboardSummaryDto(
    DashboardCountsDto Counts,
    DashboardFinanceDto Finance,
    IReadOnlyList<MonthlyTrendPointDto> Trend,
    IReadOnlyList<CourseEnrolmentDto> CourseEnrolment,
    IReadOnlyList<NoticeDto> RecentNotices,
    IReadOnlyList<RecentStudentDto> RecentAdmissions,
    IReadOnlyList<RecentPaymentDto> RecentPayments,
    DateTime GeneratedOn);

public record DashboardCountsDto(
    int ActiveStudents,
    int InactiveStudents,
    int CompletedStudents,
    int Courses,
    int Teachers,
    int StaffAccounts,
    int StudentAccounts,
    int PendingEnquiries);

/// <summary>
/// This month's money. Income is fee collections + other income; cost is institute
/// expenses plus what the teachers are paid out of those fees (<c>TecFees</c>),
/// the institute's largest running cost.
///
/// <para>
/// <b>Late fines are not added on top.</b> <c>MonthlyFees.Installment</c> is written
/// as installment + fine (verified against the data: Installment − Fine equals the
/// student's agreed monthly fee), so <see cref="LateFinesThisMonth"/> is an
/// "of which" breakdown of <see cref="FeesCollectedThisMonth"/>, not a separate
/// income line. Adding it would overstate income.
/// </para>
/// </summary>
public record DashboardFinanceDto(
    decimal FeesCollectedThisMonth,
    /// <summary>Portion of <see cref="FeesCollectedThisMonth"/> that is late fine.</summary>
    decimal LateFinesThisMonth,
    decimal OtherIncomeThisMonth,
    decimal ExpensesThisMonth,
    decimal TeacherPayoutThisMonth,
    decimal TotalIncomeThisMonth,
    decimal TotalCostThisMonth,
    decimal NetThisMonth,
    decimal OutstandingDues,
    int StudentsInArrears,
    /// <summary>
    /// Date of the most recent fee payment on record. The migrated ledger ends
    /// before the current month, so without this the dashboard would show a wall of
    /// zeros with no explanation.
    /// </summary>
    DateOnly? LastPaymentOn);

/// <summary>One point on the 12-month income-vs-cost chart.</summary>
public record MonthlyTrendPointDto(
    int Year,
    int Month,
    string Label,
    decimal Income,
    decimal Expense,
    decimal TeacherPayout);

public record CourseEnrolmentDto(int CourseId, string CourseName, int StudentCount);

public record NoticeDto(int Id, string Text, DateTime? NoticeDate);

public record RecentStudentDto(
    int Id,
    string? Name,
    string? RegistrationId,
    string? CourseName,
    DateOnly? DateOfJoining);

/// <summary>A recent fee payment, for the dashboard's collections list.</summary>
public record RecentPaymentDto(
    int Id,
    string? StudentName,
    string? RegistrationId,
    string? BillNo,
    decimal? Amount,
    DateOnly? PaidOn);
