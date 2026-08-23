using System.Globalization;
using InstituteMS.Application.Common;
using InstituteMS.Application.Income;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Income and profit-and-loss reporting. Replaces <c>IncomeController</c> plus
/// <c>Models/Income.cs</c> and <c>Models/Latefine.cs</c> (MIGRATION_PLAN §4.6, §5.8).
///
/// <para>Three corrections to the legacy behaviour, all of which changed the numbers:</para>
/// <list type="number">
///   <item>
///     <b>Late fines are no longer counted twice.</b> The legacy page showed a fee
///     income panel and a late-fine panel, each with its own running total, and the
///     operator was left to add them up. But <c>MonthlyFees.Installment</c> is
///     written as installment + fine, so the fine is already in the fee figure.
///     It is reported here as an "of which" breakdown.
///   </item>
///   <item>
///     <b><c>UNION</c> became <c>UNION ALL</c>.</b> The legacy income roll-up
///     (Models/Income.cs:39) unioned fee rows with admission-fee rows. <c>UNION</c>
///     removes duplicates, so two payments of the same amount, on the same day, for
///     the same student silently collapsed into one and the income was understated.
///   </item>
///   <item>
///     <b>No <c>Math.abs()</c> on totals.</b> Scripts/Income.js took the absolute
///     value of every row before summing, which turned credits and negative dues
///     into positive amounts.
///   </item>
/// </list>
/// </summary>
public class IncomeService : IIncomeService
{
    private readonly AppDbContext _db;

    public IncomeService(AppDbContext db) => _db = db;

    public async Task<Result<IncomeReportDto>> GetReportAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
            return Result<IncomeReportDto>.Failure("The 'to' date cannot be before the 'from' date.", ResultStatus.BadRequest);

        // Totals come straight from SQL aggregates, so they stay correct however many
        // detail rows the range contains.
        var fees = await FeeQuery(from, to).SumAsync(f => (decimal?)f.Installment ?? 0m, ct);
        var feeCount = await FeeQuery(from, to).CountAsync(ct);

        var admissions = await AdmissionQuery(from, to).SumAsync(s => (decimal?)s.AdmissionFees ?? 0m, ct);
        var admissionCount = await AdmissionQuery(from, to).CountAsync(ct);

        var otherIncome = await OtherIncomeQuery(from, to).SumAsync(i => (decimal?)i.Amount ?? 0m, ct);
        var otherIncomeCount = await OtherIncomeQuery(from, to).CountAsync(ct);

        var lateFines = await LateFineQuery(from, to).SumAsync(l => (decimal?)l.FineAmount ?? 0m, ct);
        var lateFineCount = await LateFineQuery(from, to).CountAsync(ct);

        var expenses = await ExpenseQuery(from, to).SumAsync(e => (decimal?)e.Amount ?? 0m, ct);
        var expenseCount = await ExpenseQuery(from, to).CountAsync(ct);

        var teacherPayouts = await TeacherPayoutQuery(from, to).SumAsync(t => (decimal?)t.Salary ?? 0m, ct);
        var teacherPayoutCount = await TeacherPayoutQuery(from, to).CountAsync(ct);

        var teacherExpenses = await TeacherExpenseQuery(from, to).SumAsync(t => (decimal?)t.Amount ?? 0m, ct);
        var teacherExpenseCount = await TeacherExpenseQuery(from, to).CountAsync(ct);

        // Movement in dues: how much the outstanding balance grew or shrank.
        var duesMovement = await FeeQuery(from, to)
            .SumAsync(f => ((decimal?)f.NewDue ?? 0m) - ((decimal?)f.OldDue ?? 0m), ct);

        var totalIncome = fees + admissions + otherIncome;
        var totalCost = expenses + teacherPayouts + teacherExpenses;
        var net = totalIncome - totalCost;

        var margin = totalIncome == 0m
            ? 0m
            : decimal.Round(net / totalIncome * 100m, 2, MidpointRounding.AwayFromZero);

        var sections = new List<IncomeSectionTotalDto>
        {
            new(IncomeSection.Fees, "Fee collections", fees, feeCount, false, false),
            new(IncomeSection.Admissions, "Admission fees", admissions, admissionCount, false, false),
            new(IncomeSection.OtherIncome, "Other income", otherIncome, otherIncomeCount, false, false),
            new(IncomeSection.LateFines, "of which late fines", lateFines, lateFineCount, false, true),
            new(IncomeSection.Expenses, "Institute expenses", expenses, expenseCount, true, false),
            new(IncomeSection.TeacherPayouts, "Teacher payouts", teacherPayouts, teacherPayoutCount, true, false),
            new(IncomeSection.TeacherExpenses, "Teacher expenses", teacherExpenses, teacherExpenseCount, true, false),
            new(IncomeSection.Dues, "Dues movement", duesMovement, feeCount, false, true)
        };

        var monthly = await GetMonthlyAsync(from, to, ct);

        return Result<IncomeReportDto>.Success(new IncomeReportDto(
            from, to,
            fees, admissions, otherIncome, totalIncome,
            lateFines,
            expenses, teacherPayouts, teacherExpenses, totalCost,
            net, margin, duesMovement,
            sections, monthly, DateTime.UtcNow));
    }

    public async Task<PagedResult<IncomeDetailDto>> GetDetailAsync(
        IncomeDetailQuery query, CancellationToken ct = default)
    {
        var (from, to) = (query.From, query.To);
        var skip = (query.Page - 1) * query.PageSize;

        switch (query.Section)
        {
            case IncomeSection.Fees:
            case IncomeSection.Dues:
            {
                var source = FeeQuery(from, to);
                var total = await source.CountAsync(ct);

                var rows = await source
                    .OrderByDescending(f => f.FeeDate).ThenByDescending(f => f.Id)
                    .Skip(skip).Take(query.PageSize)
                    .Select(f => new
                    {
                        f.Id, f.FeeDate, f.BillNo, f.StudentRegistrationId,
                        f.Installment, f.NewDue, f.OldDue
                    })
                    .ToListAsync(ct);

                var names = await StudentNamesAsync(rows.Select(r => r.StudentRegistrationId), ct);
                var isDues = query.Section == IncomeSection.Dues;

                var items = rows.Select(r => new IncomeDetailDto(
                    r.Id, r.FeeDate, r.BillNo,
                    Name(names, r.StudentRegistrationId), r.StudentRegistrationId,
                    Course(names, r.StudentRegistrationId), null,
                    isDues ? "Due movement" : "Monthly fee",
                    isDues
                        ? ((r.NewDue ?? 0m) - (r.OldDue ?? 0m))
                        : (r.Installment ?? 0m))).ToList();

                return PagedResult<IncomeDetailDto>.Create(items, query.Page, query.PageSize, total);
            }

            case IncomeSection.Admissions:
            {
                var source = AdmissionQuery(from, to);
                var total = await source.CountAsync(ct);

                var items = await source
                    .OrderByDescending(s => s.DateOfJoining).ThenByDescending(s => s.Id)
                    .Skip(skip).Take(query.PageSize)
                    .Select(s => new IncomeDetailDto(
                        s.Id, s.DateOfJoining, s.BillNo, s.Name, s.RegistrationId,
                        s.Course != null ? s.Course.Name : null, null,
                        "Admission fee", s.AdmissionFees ?? 0m))
                    .ToListAsync(ct);

                return PagedResult<IncomeDetailDto>.Create(items, query.Page, query.PageSize, total);
            }

            case IncomeSection.OtherIncome:
            {
                var source = OtherIncomeQuery(from, to);
                var total = await source.CountAsync(ct);

                var items = await source
                    .OrderByDescending(i => i.IncomeDate).ThenByDescending(i => i.Id)
                    .Skip(skip).Take(query.PageSize)
                    .Select(i => new IncomeDetailDto(
                        i.Id, i.IncomeDate, null, null, null, null, null,
                        i.Purpose, i.Amount ?? 0m))
                    .ToListAsync(ct);

                return PagedResult<IncomeDetailDto>.Create(items, query.Page, query.PageSize, total);
            }

            case IncomeSection.LateFines:
            {
                var source = LateFineQuery(from, to);
                var total = await source.CountAsync(ct);

                var rows = await source
                    .OrderByDescending(l => l.FineDate).ThenByDescending(l => l.Id)
                    .Skip(skip).Take(query.PageSize)
                    .Select(l => new { l.Id, l.FineDate, l.BillNo, l.StudentRegistrationId, l.FineAmount })
                    .ToListAsync(ct);

                var names = await StudentNamesAsync(rows.Select(r => r.StudentRegistrationId), ct);

                var items = rows.Select(r => new IncomeDetailDto(
                    r.Id, r.FineDate, r.BillNo,
                    Name(names, r.StudentRegistrationId), r.StudentRegistrationId,
                    Course(names, r.StudentRegistrationId), null,
                    "Late fine", r.FineAmount ?? 0m)).ToList();

                return PagedResult<IncomeDetailDto>.Create(items, query.Page, query.PageSize, total);
            }

            case IncomeSection.Expenses:
            {
                var source = ExpenseQuery(from, to);
                var total = await source.CountAsync(ct);

                var items = await source
                    .OrderByDescending(e => e.ExpenseDate).ThenByDescending(e => e.Id)
                    .Skip(skip).Take(query.PageSize)
                    .Select(e => new IncomeDetailDto(
                        e.Id, e.ExpenseDate, null, null, null, null, null,
                        e.Details, e.Amount ?? 0m))
                    .ToListAsync(ct);

                return PagedResult<IncomeDetailDto>.Create(items, query.Page, query.PageSize, total);
            }

            case IncomeSection.TeacherPayouts:
            {
                var source = TeacherPayoutQuery(from, to);
                var total = await source.CountAsync(ct);

                var rows = await source
                    .OrderByDescending(t => t.PaidOn).ThenByDescending(t => t.Id)
                    .Skip(skip).Take(query.PageSize)
                    .Select(t => new
                    {
                        t.Id, t.PaidOn, t.BillNo, t.StudentRegistrationId, t.Salary,
                        TeacherName = t.Teacher != null ? t.Teacher.Name : null
                    })
                    .ToListAsync(ct);

                var names = await StudentNamesAsync(rows.Select(r => r.StudentRegistrationId), ct);

                var items = rows.Select(r => new IncomeDetailDto(
                    r.Id, r.PaidOn, r.BillNo,
                    Name(names, r.StudentRegistrationId), r.StudentRegistrationId,
                    Course(names, r.StudentRegistrationId), r.TeacherName,
                    "Teacher share", r.Salary ?? 0m)).ToList();

                return PagedResult<IncomeDetailDto>.Create(items, query.Page, query.PageSize, total);
            }

            case IncomeSection.TeacherExpenses:
            {
                var source = TeacherExpenseQuery(from, to);
                var total = await source.CountAsync(ct);

                var items = await source
                    .OrderByDescending(t => t.ExpenseDate).ThenByDescending(t => t.Id)
                    .Skip(skip).Take(query.PageSize)
                    .Select(t => new IncomeDetailDto(
                        t.Id, t.ExpenseDate, null, null, null, null,
                        t.Teacher != null ? t.Teacher.Name : null,
                        "Teacher expense", t.Amount ?? 0m))
                    .ToListAsync(ct);

                return PagedResult<IncomeDetailDto>.Create(items, query.Page, query.PageSize, total);
            }

            default:
                return PagedResult<IncomeDetailDto>.Create([], query.Page, query.PageSize, 0);
        }
    }

    /* ------------------------------------------------------------- monthly */

    private async Task<IReadOnlyList<IncomeTrendPointDto>> GetMonthlyAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
    {
        // Cap the chart so a decade-wide range does not return hundreds of points.
        var months = ((to.Year - from.Year) * 12) + to.Month - from.Month + 1;
        if (months is < 1 or > 36) return [];

        var feeByMonth = await FeeQuery(from, to)
            .GroupBy(f => new { f.FeeDate!.Value.Year, f.FeeDate!.Value.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Installment ?? 0m)))
            .ToListAsync(ct);

        var admissionByMonth = await AdmissionQuery(from, to)
            .GroupBy(s => new { s.DateOfJoining!.Value.Year, s.DateOfJoining!.Value.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.AdmissionFees ?? 0m)))
            .ToListAsync(ct);

        var otherByMonth = await OtherIncomeQuery(from, to)
            .GroupBy(i => new { i.IncomeDate!.Value.Year, i.IncomeDate!.Value.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Amount ?? 0m)))
            .ToListAsync(ct);

        var expenseByMonth = await ExpenseQuery(from, to)
            .GroupBy(e => new { e.ExpenseDate!.Value.Year, e.ExpenseDate!.Value.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Amount ?? 0m)))
            .ToListAsync(ct);

        var payoutByMonth = await TeacherPayoutQuery(from, to)
            .GroupBy(t => new { t.PaidOn!.Value.Year, t.PaidOn!.Value.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Salary ?? 0m)))
            .ToListAsync(ct);

        var teacherExpenseByMonth = await TeacherExpenseQuery(from, to)
            .GroupBy(t => new { t.ExpenseDate!.Value.Year, t.ExpenseDate!.Value.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Amount ?? 0m)))
            .ToListAsync(ct);

        var points = new List<IncomeTrendPointDto>(months);

        for (var cursor = new DateOnly(from.Year, from.Month, 1);
             cursor <= new DateOnly(to.Year, to.Month, 1);
             cursor = cursor.AddMonths(1))
        {
            var income = Total(feeByMonth, cursor) + Total(admissionByMonth, cursor) + Total(otherByMonth, cursor);
            var cost = Total(expenseByMonth, cursor) + Total(payoutByMonth, cursor) + Total(teacherExpenseByMonth, cursor);

            var label = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(cursor.Month);
            points.Add(new IncomeTrendPointDto(cursor.Year, cursor.Month, label, income, cost, income - cost));
        }

        return points;

        static decimal Total(List<MonthBucket> source, DateOnly month) =>
            source.FirstOrDefault(x => x.Year == month.Year && x.Month == month.Month)?.Amount ?? 0m;
    }

    private sealed record MonthBucket(int Year, int Month, decimal Amount);

    /* ------------------------------------------------- the source queries */
    /* One definition each, shared by the totals, the chart and the detail
       lists, so a figure can never disagree with the rows behind it. Ranges
       are inclusive of both ends, matching the legacy BETWEEN. */

    private IQueryable<Domain.Entities.MonthlyFee> FeeQuery(DateOnly from, DateOnly to) =>
        _db.MonthlyFees.AsNoTracking().Where(f => f.FeeDate >= from && f.FeeDate <= to);

    /// <summary>
    /// Admission fees of students who joined in the range. The legacy roll-up
    /// restricted this to active students, so a student who has since left drops out
    /// of a historic report; that behaviour is preserved.
    /// </summary>
    private IQueryable<Domain.Entities.Student> AdmissionQuery(DateOnly from, DateOnly to) =>
        _db.Students.AsNoTracking()
            .Where(s => s.IsActive && s.DateOfJoining >= from && s.DateOfJoining <= to);

    private IQueryable<Domain.Entities.OtherIncome> OtherIncomeQuery(DateOnly from, DateOnly to) =>
        _db.OtherIncomes.AsNoTracking().Where(i => i.IncomeDate >= from && i.IncomeDate <= to);

    private IQueryable<Domain.Entities.LateFee> LateFineQuery(DateOnly from, DateOnly to) =>
        _db.LateFees.AsNoTracking().Where(l => l.FineDate >= from && l.FineDate <= to);

    private IQueryable<Domain.Entities.Expense> ExpenseQuery(DateOnly from, DateOnly to) =>
        _db.Expenses.AsNoTracking().Where(e => e.ExpenseDate >= from && e.ExpenseDate <= to);

    private IQueryable<Domain.Entities.TeacherFee> TeacherPayoutQuery(DateOnly from, DateOnly to) =>
        _db.TeacherFees.AsNoTracking().Where(t => t.PaidOn >= from && t.PaidOn <= to);

    private IQueryable<Domain.Entities.TeacherExpense> TeacherExpenseQuery(DateOnly from, DateOnly to) =>
        _db.TeacherExpenses.AsNoTracking().Where(t => t.ExpenseDate >= from && t.ExpenseDate <= to);

    /* --------------------------------------------------------------- names */

    private sealed record StudentName(string? Name, string? CourseName);

    /// <summary>
    /// Resolves student names for a page of rows in one query. Registration ids are
    /// not unique in the migrated data, so the first match is taken.
    /// </summary>
    private async Task<Dictionary<string, StudentName>> StudentNamesAsync(
        IEnumerable<string?> registrationIds, CancellationToken ct)
    {
        var ids = registrationIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
        if (ids.Count == 0) return [];

        var rows = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId != null && ids.Contains(s.RegistrationId))
            .Select(s => new
            {
                s.RegistrationId,
                s.Name,
                CourseName = s.Course != null ? s.Course.Name : null
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.RegistrationId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new StudentName(g.First().Name, g.First().CourseName),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? Name(Dictionary<string, StudentName> names, string? registrationId) =>
        registrationId is not null && names.TryGetValue(registrationId, out var v) ? v.Name : null;

    private static string? Course(Dictionary<string, StudentName> names, string? registrationId) =>
        registrationId is not null && names.TryGetValue(registrationId, out var v) ? v.CourseName : null;
}
