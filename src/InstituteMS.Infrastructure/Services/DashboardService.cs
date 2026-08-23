using System.Globalization;
using InstituteMS.Application.Dashboard;
using InstituteMS.Domain.Common;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Aggregates the Home Dashboard in one call. Every query is parameterized LINQ —
/// no string concatenation anywhere (defect D2).
///
/// Since the finance tables were migrated (db/migrations/008) this reports the
/// institute's real position: fee collections and late fines in, institute
/// expenses and teacher payouts (<c>TecFees</c>) out.
/// </summary>
public class DashboardService : IDashboardService
{
    private const int TrendMonths = 12;

    private readonly AppDbContext _db;

    public DashboardService(AppDbContext db) => _db = db;

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        var trendStart = monthStart.AddMonths(-(TrendMonths - 1));

        var counts = await GetCountsAsync(ct);
        var finance = await GetFinanceAsync(monthStart, nextMonthStart, ct);
        var trend = await GetTrendAsync(trendStart, nextMonthStart, ct);
        var enrolment = await GetCourseEnrolmentAsync(ct);
        var notices = await GetRecentNoticesAsync(ct);
        var admissions = await GetRecentAdmissionsAsync(ct);
        var payments = await GetRecentPaymentsAsync(ct);

        return new DashboardSummaryDto(
            counts, finance, trend, enrolment, notices, admissions, payments, DateTime.UtcNow);
    }

    private async Task<DashboardCountsDto> GetCountsAsync(CancellationToken ct)
    {
        var activeStudents = await _db.Students.CountAsync(s => s.IsActive, ct);
        var inactiveStudents = await _db.Students.CountAsync(s => !s.IsActive, ct);
        var completedStudents = await _db.Students.CountAsync(s => s.IsComplete, ct);
        var courses = await _db.Courses.CountAsync(ct);

        // Placeholder rows stand in for teachers deleted from the legacy system while
        // still referenced; they are not real headcount.
        var teachers = await _db.Teachers.CountAsync(t => t.CreatedBy != "migration-004-orphan", ct);

        var studentAccounts = await _db.Users
            .CountAsync(u => u.IsActive && u.Roles != null && u.Roles.Contains(UserRoles.Student), ct);

        var staffAccounts = await _db.Users
            .CountAsync(u => u.IsActive && (u.Roles == null || !u.Roles.Contains(UserRoles.Student)), ct);

        var enquiries = await _db.ContactMessages.CountAsync(m => !m.IsHandled, ct);

        return new DashboardCountsDto(
            activeStudents, inactiveStudents, completedStudents,
            courses, teachers, staffAccounts, studentAccounts, enquiries);
    }

    private async Task<DashboardFinanceDto> GetFinanceAsync(
        DateOnly monthStart, DateOnly nextMonthStart, CancellationToken ct)
    {
        var fees = await _db.MonthlyFees
            .Where(f => f.FeeDate >= monthStart && f.FeeDate < nextMonthStart)
            .SumAsync(f => (decimal?)f.Installment ?? 0m, ct);

        var lateFines = await _db.LateFees
            .Where(l => l.FineDate >= monthStart && l.FineDate < nextMonthStart)
            .SumAsync(l => (decimal?)l.FineAmount ?? 0m, ct);

        var otherIncome = await _db.OtherIncomes
            .Where(i => i.IncomeDate >= monthStart && i.IncomeDate < nextMonthStart)
            .SumAsync(i => (decimal?)i.Amount ?? 0m, ct);

        var expenses = await _db.Expenses
            .Where(e => e.ExpenseDate >= monthStart && e.ExpenseDate < nextMonthStart)
            .SumAsync(e => (decimal?)e.Amount ?? 0m, ct);

        var teacherFees = await _db.TeacherFees
            .Where(t => t.PaidOn >= monthStart && t.PaidOn < nextMonthStart)
            .SumAsync(t => (decimal?)t.Salary ?? 0m, ct);

        var teacherExpenses = await _db.TeacherExpenses
            .Where(t => t.ExpenseDate >= monthStart && t.ExpenseDate < nextMonthStart)
            .SumAsync(t => (decimal?)t.Amount ?? 0m, ct);

        var teacherPayout = teacherFees + teacherExpenses;

        var (outstanding, inArrears) = await GetOutstandingAsync(ct);

        var lastPaymentOn = await _db.MonthlyFees
            .Select(f => f.PaymentDate ?? f.FeeDate)
            .MaxAsync(ct);

        // lateFines is deliberately NOT added: MonthlyFees.Installment is stored as
        // installment + fine, so it is already inside `fees`. It is reported
        // separately only as an "of which" breakdown.
        var totalIncome = fees + otherIncome;
        var totalCost = expenses + teacherPayout;

        return new DashboardFinanceDto(
            fees, lateFines, otherIncome, expenses, teacherPayout,
            totalIncome, totalCost, totalIncome - totalCost,
            outstanding, inArrears, lastPaymentOn);
    }

    /// <summary>
    /// Outstanding dues = the latest <c>NewDue</c> recorded against each active
    /// student. Negative values are ignored: the legacy ledger sometimes carries a
    /// credit forward, which is not money owed to the institute.
    /// </summary>
    private async Task<(decimal Outstanding, int StudentsInArrears)> GetOutstandingAsync(CancellationToken ct)
    {
        var activeRegIds = _db.Students
            .Where(s => s.IsActive && s.RegistrationId != null)
            .Select(s => s.RegistrationId!);

        var latestDues = await _db.MonthlyFees
            .Where(f => f.StudentRegistrationId != null && activeRegIds.Contains(f.StudentRegistrationId))
            .GroupBy(f => f.StudentRegistrationId!)
            .Select(g => g.OrderByDescending(f => f.FeeDate).ThenByDescending(f => f.Id)
                          .Select(f => f.NewDue).FirstOrDefault())
            .ToListAsync(ct);

        var owed = latestDues.Where(d => d is > 0m).Select(d => d!.Value).ToList();

        return (owed.Sum(), owed.Count);
    }

    private async Task<IReadOnlyList<MonthlyTrendPointDto>> GetTrendAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        var feeByMonth = await _db.MonthlyFees
            .Where(f => f.FeeDate >= from && f.FeeDate < to)
            .GroupBy(f => new { f.FeeDate!.Value.Year, f.FeeDate!.Value.Month })
            .Select(g => new MonthTotal(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Installment ?? 0m)))
            .ToListAsync(ct);

        var otherByMonth = await _db.OtherIncomes
            .Where(i => i.IncomeDate >= from && i.IncomeDate < to)
            .GroupBy(i => new { i.IncomeDate!.Value.Year, i.IncomeDate!.Value.Month })
            .Select(g => new MonthTotal(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Amount ?? 0m)))
            .ToListAsync(ct);

        var expenseByMonth = await _db.Expenses
            .Where(e => e.ExpenseDate >= from && e.ExpenseDate < to)
            .GroupBy(e => new { e.ExpenseDate!.Value.Year, e.ExpenseDate!.Value.Month })
            .Select(g => new MonthTotal(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Amount ?? 0m)))
            .ToListAsync(ct);

        var payoutByMonth = await _db.TeacherFees
            .Where(t => t.PaidOn >= from && t.PaidOn < to)
            .GroupBy(t => new { t.PaidOn!.Value.Year, t.PaidOn!.Value.Month })
            .Select(g => new MonthTotal(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Salary ?? 0m)))
            .ToListAsync(ct);

        // Materialise every month in the window so the chart has no gaps.
        var points = new List<MonthlyTrendPointDto>(TrendMonths);

        for (var cursor = from; cursor < to; cursor = cursor.AddMonths(1))
        {
            // Fines are already inside the fee totals — see GetFinanceAsync.
            var income = Total(feeByMonth, cursor) + Total(otherByMonth, cursor);
            var expense = Total(expenseByMonth, cursor);
            var payout = Total(payoutByMonth, cursor);

            var label = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(cursor.Month);
            points.Add(new MonthlyTrendPointDto(cursor.Year, cursor.Month, label, income, expense, payout));
        }

        return points;

        static decimal Total(List<MonthTotal> source, DateOnly month) =>
            source.FirstOrDefault(x => x.Year == month.Year && x.Month == month.Month)?.Total ?? 0m;
    }

    /// <summary>Per-month rollup shared by the five trend queries.</summary>
    private sealed record MonthTotal(int Year, int Month, decimal Total);

    private async Task<IReadOnlyList<CourseEnrolmentDto>> GetCourseEnrolmentAsync(CancellationToken ct)
    {
        // Ordering happens on the anonymous projection — EF cannot sort by a
        // member of a constructor-projected record, so the DTO is built after.
        var rows = await _db.Courses
            .AsNoTracking()
            .Select(c => new
            {
                c.Cid,
                c.Name,
                StudentCount = c.Students.Count(s => s.IsActive)
            })
            .Where(c => c.StudentCount > 0)
            .OrderByDescending(c => c.StudentCount)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

        return rows
            .Select(r => new CourseEnrolmentDto(r.Cid, r.Name ?? "(unnamed)", r.StudentCount))
            .ToList();
    }

    private async Task<IReadOnlyList<NoticeDto>> GetRecentNoticesAsync(CancellationToken ct) =>
        await _db.Notices
            .AsNoTracking()
            .OrderByDescending(n => n.NoticeDate)
            .Take(5)
            .Select(n => new NoticeDto(n.Id, n.Text ?? string.Empty, n.NoticeDate))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<RecentStudentDto>> GetRecentAdmissionsAsync(CancellationToken ct) =>
        await _db.Students
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderByDescending(s => s.DateOfJoining)
            .ThenByDescending(s => s.Id)
            .Take(5)
            .Select(s => new RecentStudentDto(
                s.Id, s.Name, s.RegistrationId, s.Course != null ? s.Course.Name : null, s.DateOfJoining))
            .ToListAsync(ct);

    /// <summary>
    /// Latest fee collections. The student name comes from a join on
    /// <c>StuRegId</c>, and because the legacy data has duplicate registration ids
    /// the first match is taken rather than assuming one.
    /// </summary>
    private async Task<IReadOnlyList<RecentPaymentDto>> GetRecentPaymentsAsync(CancellationToken ct)
    {
        var rows = await _db.MonthlyFees
            .AsNoTracking()
            .OrderByDescending(f => f.PaymentDate ?? f.FeeDate)
            .ThenByDescending(f => f.Id)
            .Take(5)
            .Select(f => new
            {
                f.Id,
                f.StudentRegistrationId,
                f.BillNo,
                f.Installment,
                PaidOn = f.PaymentDate ?? f.FeeDate,
                StudentName = _db.Students
                    .Where(s => s.RegistrationId == f.StudentRegistrationId)
                    .Select(s => s.Name)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new RecentPaymentDto(
                r.Id, r.StudentName, r.StudentRegistrationId, r.BillNo, r.Installment, r.PaidOn))
            .ToList();
    }
}
