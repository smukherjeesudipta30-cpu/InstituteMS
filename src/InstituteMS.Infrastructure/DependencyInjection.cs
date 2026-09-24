using InstituteMS.Application.About;
using InstituteMS.Application.Academics;
using InstituteMS.Application.Attendance;
using InstituteMS.Application.Auth;
using InstituteMS.Application.Chat;
using InstituteMS.Application.Common.Interfaces;
using InstituteMS.Application.Competitions;
using InstituteMS.Application.Dashboard;
using InstituteMS.Application.Documents;
using InstituteMS.Application.Fees;
using InstituteMS.Application.Income;
using InstituteMS.Application.Lookups;
using InstituteMS.Application.Masters;
using InstituteMS.Application.Notifications;
using InstituteMS.Application.Org;
using InstituteMS.Application.Enquiries;
using InstituteMS.Application.Expenses;
using InstituteMS.Application.Exams;
using InstituteMS.Application.Reports;
using InstituteMS.Application.Students;
using InstituteMS.Application.TeacherExams;
using InstituteMS.Application.Users;
using InstituteMS.Infrastructure.Documents;
using InstituteMS.Infrastructure.Persistence;
using InstituteMS.Infrastructure.Security;
using InstituteMS.Infrastructure.Services;
using InstituteMS.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InstituteMS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
                sql.CommandTimeout(60);
            }));

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<InstituteProfileOptions>(configuration.GetSection(InstituteProfileOptions.SectionName));
        services.Configure<PaymentSettings>(configuration.GetSection(PaymentSettings.SectionName));
        services.Configure<MailOptions>(configuration.GetSection(MailOptions.SectionName));
        services.Configure<AttendanceOptions>(configuration.GetSection(AttendanceOptions.SectionName));
        services.Configure<OrgOptions>(configuration.GetSection(OrgOptions.SectionName));

        services.Configure<CompetitionOptions>(configuration.GetSection(CompetitionOptions.SectionName));

        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAboutService, AboutService>();
        services.AddScoped<IStudentService, StudentService>();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IFeeService, FeeService>();
        services.AddScoped<IIncomeService, IncomeService>();
        services.AddScoped<ITeacherService, TeacherService>();
        services.AddScoped<IReceptionistService, ReceptionistService>();
        services.AddScoped<IOtherIncomeService, OtherIncomeService>();
        services.AddScoped<IStudentMailer, SmtpStudentMailer>();
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<ICourseService, CourseService>();
        services.AddScoped<IMarkService, MarkService>();
        services.AddScoped<INoticeService, NoticeService>();
        services.AddScoped<IEnquiryService, EnquiryService>();
        services.AddSingleton<IReceiptPdfGenerator, ReceiptPdfGenerator>();
        services.AddScoped<IReceiptLinkService, ReceiptLinkService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IExamService, ExamService>();
        services.AddSingleton<AttendanceTokenService>();
        services.AddScoped<IAttendanceService, AttendanceService>();
        services.AddScoped<IOrgService, OrgService>();
        services.AddScoped<IChatService, ChatService>();

        services.AddScoped<ICompetitionService, CompetitionService>();
        services.AddSingleton<IChestCardPdfGenerator, ChestCardPdfGenerator>();
        services.AddScoped<RegistrationNumberService>();
        services.AddScoped<StudentLoginGenerator>();

        services.AddScoped<ITeacherExamService, TeacherExamService>();
        services.AddHttpClient<IExamQuestionGenerator, AnthropicExamQuestionGenerator>();

        return services;
    }
}
