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
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<OtherIncome> OtherIncomes => Set<OtherIncome>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<LateFee> LateFees => Set<LateFee>();
    public DbSet<TeacherFee> TeacherFees => Set<TeacherFee>();
    public DbSet<TeacherExpense> TeacherExpenses => Set<TeacherExpense>();
    public DbSet<Mark> Marks => Set<Mark>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<FeeTotal> FeeTotals => Set<FeeTotal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
