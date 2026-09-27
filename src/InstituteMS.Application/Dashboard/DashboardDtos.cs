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

    /// <summary>Unhandled enquiries, newest first, so they can actually be read.</summary>
    IReadOnlyList<EnquiryDto> PendingEnquiryList,

    /// <summary>Unread first, then newest. Capped — the full list has its own page.</summary>
    IReadOnlyList<AdminNotificationDto> Notifications,

    /// <summary>Drives the badge, and counts every unread one, not just those shown.</summary>
    int UnreadNotifications,

    DateTime GeneratedOn);

/// <summary>One dashboard alert for the office.</summary>
public record AdminNotificationDto(
    int Id,
    string Type,
    string Title,
    string? StudentName,
    string? CourseName,
    string? RegistrationId,
    string? LoginId,

    /// <summary>
    /// For a password change, the student's chosen password in clear. Kept out of
    /// sight in the UI behind a reveal, because a dashboard is a screen other people
    /// can see.
    /// </summary>
    string? DetailValue,

    DateTime CreatedOn,
    bool IsRead);

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
    DateOnly? LastPaymentOn,

    /// <summary>
    /// The pay cycle these figures cover (e.g. 26 Aug to 25 Sep with the default
    /// start day of 26), named by the month it ends in — "September 2026" — the
    /// same way the pay forms name it. The "ThisMonth" fields above are this cycle.
    /// </summary>
    DateOnly CycleStart = default,
    DateOnly CycleEnd = default,
    string CycleLabel = "",

    /// <summary>Admission fees received in the cycle (inside the total income).</summary>
    decimal AdmissionFees = 0m,
    /// <summary>Exam fees received in the cycle (inside the total income).</summary>
    decimal ExamFees = 0m,
    /// <summary>Receptionists' pay in the cycle (inside the total cost).</summary>
    decimal ReceptionistPay = 0m,
    /// <summary>
    /// Every other cost in the total — referral commission and administrative
    /// expenses — as one unnamed figure, so the dashboard adds up exactly to the
    /// Income report without naming anything a back-office viewer should not see.
    /// </summary>
    decimal OtherCosts = 0m,

    /// <summary>The pay cycle start day (26 by default) — the cycle runs to the day before, next month.</summary>
    int PayCycleStartDay = 26);

/// <summary>
/// One pay cycle on the 12-cycle income-vs-cost chart. Year and Month name the
/// month the cycle ends in; Expense is every cost other than the teachers' share.
/// </summary>
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

/// <summary>
/// One unhandled enquiry from the public front page's "Send Us Message" form
/// (<c>dbo.CusMessage</c>). The dashboard counted these but never showed them, so
/// the messages sat unread with only a badge to say they existed.
/// </summary>
public record EnquiryDto(
    int Id,
    string? Name,
    string? Email,
    string? Phone,
    string? Message,
    DateTime SubmittedOn,

    /// <summary>True once somebody has marked it read. Read ones leave the dashboard.</summary>
    bool IsHandled = false);

/// <summary>Which enquiries to list.</summary>
public enum EnquiryFilter
{
    /// <summary>Not yet marked read -- what the dashboard panel shows.</summary>
    New = 0,

    /// <summary>Already marked read, so they can be found again afterwards.</summary>
    Read = 1,

    All = 2
}

/// <summary>A page of enquiries from the public contact form.</summary>
public record EnquiryListDto(
    IReadOnlyList<EnquiryDto> Items,
    int TotalCount,
    int NewCount,
    int ReadCount);
