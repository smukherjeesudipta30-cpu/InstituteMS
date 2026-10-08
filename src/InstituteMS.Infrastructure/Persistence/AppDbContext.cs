using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<StudentPhoto> StudentPhotos => Set<StudentPhoto>();
    public DbSet<MonthlyFee> MonthlyFees => Set<MonthlyFee>();
    public DbSet<AdmissionFeePayment> AdmissionFeePayments => Set<AdmissionFeePayment>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<OtherIncome> OtherIncomes => Set<OtherIncome>();

    /* The side tables. dbo.Expense and dbo.OIncome cannot gain columns without
       breaking the legacy application's column-list-free INSERTs. */
    public DbSet<ExpenseExt> ExpenseExts => Set<ExpenseExt>();
    public DbSet<OtherIncomeExt> OtherIncomeExts => Set<OtherIncomeExt>();
    public DbSet<ContactMessageExt> ContactMessageExts => Set<ContactMessageExt>();
    public DbSet<MailLogEntry> MailLog => Set<MailLogEntry>();
    public DbSet<AdminNotification> AdminNotifications => Set<AdminNotification>();
    public DbSet<PayrollSettings> PayrollSettings => Set<PayrollSettings>();
    public DbSet<CourseEnquiry> CourseEnquiries => Set<CourseEnquiry>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<DailyRoutine> DailyRoutines => Set<DailyRoutine>();
    public DbSet<ExamPaper> ExamPapers => Set<ExamPaper>();
    public DbSet<ExamQuestion> ExamQuestions => Set<ExamQuestion>();
    public DbSet<ExamAttempt> ExamAttempts => Set<ExamAttempt>();
    public DbSet<ExamAttemptAnswer> ExamAttemptAnswers => Set<ExamAttemptAnswer>();
    public DbSet<ExamAnswerCopy> ExamAnswerCopies => Set<ExamAnswerCopy>();
    public DbSet<ReferralSlab> ReferralSlabs => Set<ReferralSlab>();
    public DbSet<ReferralCredit> ReferralCredits => Set<ReferralCredit>();
    public DbSet<StudentReferrer> StudentReferrers => Set<StudentReferrer>();
    public DbSet<ExamFeeRate> ExamFeeRates => Set<ExamFeeRate>();
    public DbSet<ProprietorHonorarium> ProprietorHonoraria => Set<ProprietorHonorarium>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<HolidayYearImport> HolidayYearImports => Set<HolidayYearImport>();
    public DbSet<BankStatement> BankStatements => Set<BankStatement>();
    public DbSet<JobVacancy> JobVacancies => Set<JobVacancy>();
    public DbSet<JobApplication> JobApplications => Set<JobApplication>();
    public DbSet<ExamFeePayment> ExamFeePayments => Set<ExamFeePayment>();
    public DbSet<ExamSchedule> ExamSchedules => Set<ExamSchedule>();
    public DbSet<CertificateIssue> CertificateIssues => Set<CertificateIssue>();
    public DbSet<Receptionist> Receptionists => Set<Receptionist>();
    public DbSet<ReceptionistPayment> ReceptionistPayments => Set<ReceptionistPayment>();
    public DbSet<ReceptionistLeaveDeduction> ReceptionistLeaveDeductions => Set<ReceptionistLeaveDeduction>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<LateFee> LateFees => Set<LateFee>();
    public DbSet<TeacherFee> TeacherFees => Set<TeacherFee>();
    public DbSet<TeacherExpense> TeacherExpenses => Set<TeacherExpense>();
    public DbSet<Mark> Marks => Set<Mark>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<TeacherExamAssignment> TeacherExamAssignments => Set<TeacherExamAssignment>();
    public DbSet<TeacherExamSchedule> TeacherExamSchedules => Set<TeacherExamSchedule>();
    public DbSet<TeacherExamQuestion> TeacherExamQuestions => Set<TeacherExamQuestion>();
    public DbSet<TeacherExamAttempt> TeacherExamAttempts => Set<TeacherExamAttempt>();
    public DbSet<TeacherExamAnswer> TeacherExamAnswers => Set<TeacherExamAnswer>();
    public DbSet<StaffAttendance> StaffAttendance => Set<StaffAttendance>();
    public DbSet<AttendanceShift> AttendanceShifts => Set<AttendanceShift>();
    public DbSet<AttendanceChallenge> AttendanceChallenges => Set<AttendanceChallenge>();
    public DbSet<StaffShiftAssignment> StaffShiftAssignments => Set<StaffShiftAssignment>();
    public DbSet<Competition> Competitions => Set<Competition>();
    public DbSet<CompetitionGroup> CompetitionGroups => Set<CompetitionGroup>();
    public DbSet<CompetitionOrganiser> CompetitionOrganisers => Set<CompetitionOrganiser>();
    public DbSet<CompetitionApplicant> CompetitionApplicants => Set<CompetitionApplicant>();
    public DbSet<CompetitionEntry> CompetitionEntries => Set<CompetitionEntry>();
    public DbSet<CompetitionDocument> CompetitionDocuments => Set<CompetitionDocument>();
    public DbSet<CompetitionAdvert> CompetitionAdverts => Set<CompetitionAdvert>();
    public DbSet<CompetitionDraft> CompetitionDrafts => Set<CompetitionDraft>();

    public DbSet<OrgNode> OrgNodes => Set<OrgNode>();
    public DbSet<OrgEdge> OrgEdges => Set<OrgEdge>();
    public DbSet<FeeTotal> FeeTotals => Set<FeeTotal>();

    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();
    public DbSet<ChatParticipant> ChatParticipants => Set<ChatParticipant>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatMessageHidden> ChatMessagesHidden => Set<ChatMessageHidden>();
    public DbSet<ChatPushSubscription> ChatPushSubscriptions => Set<ChatPushSubscription>();
    public DbSet<ChatFcmSubscription> ChatFcmSubscriptions => Set<ChatFcmSubscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Applies the legacy-schema conversions that are true of every entity, so
    /// they are stated once rather than on each of the seventeen date properties.
    /// </summary>
    /// <remarks>
    /// EF Core 8 maps <see cref="DateOnly"/> to a <c>date</c> column. The
    /// database this runs against is the live legacy one, where every date is
    /// <c>datetime</c>, so without this the model does not build at all.
    ///
    /// A convention is used rather than per-property calls because the failure
    /// mode of missing one is a runtime translation error on a single screen -
    /// exactly the kind of gap that survives a code review and shows up in
    /// front of a user.
    /// </remarks>
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        base.ConfigureConventions(builder);

        builder.Properties<DateOnly>().HaveConversion<DateOnlyToDateTimeConverter>();
        builder.Properties<DateOnly?>().HaveConversion<NullableDateOnlyToDateTimeConverter>();
    }
}
