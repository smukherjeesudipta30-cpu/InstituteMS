using InstituteMS.Application.About;
using InstituteMS.Application.Academics;
using InstituteMS.Application.Auth;
using InstituteMS.Application.Common.Interfaces;
using InstituteMS.Application.Dashboard;
using InstituteMS.Application.Fees;
using InstituteMS.Application.Income;
using InstituteMS.Application.Lookups;
using InstituteMS.Application.Masters;
using InstituteMS.Application.Expenses;
using InstituteMS.Application.Reports;
using InstituteMS.Application.Students;
using InstituteMS.Application.Users;
using InstituteMS.Infrastructure.Persistence;
using InstituteMS.Infrastructure.Security;
using InstituteMS.Infrastructure.Services;
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
        services.AddScoped<IBranchService, BranchService>();
        services.AddScoped<ICourseService, CourseService>();
        services.AddScoped<IMarkService, MarkService>();
        services.AddScoped<INoticeService, NoticeService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<RegistrationNumberService>();

        return services;
    }
}
