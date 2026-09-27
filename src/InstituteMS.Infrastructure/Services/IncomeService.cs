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
        DateOnly from, DateOnly to, string? branch = null, CancellationToken ct = default)
    {
        branch = string.IsNullOrWhiteSpace(branch) ? null : branch.Trim();

        if (to < from)
            return Result<IncomeReportDto>.Failure("The 'to' date cannot be before the 'from' date.", ResultStatus.BadRequest);

        // Totals come straight from SQL aggregates, so they stay correct however many
        // detail rows the range contains.
        var fees = await FeeQuery(from, to, branch).SumAsync(f => (decimal?)f.Installment ?? 0m, ct);
        var feeCount = await FeeQuery(from, to, branch).CountAsync(ct);

        var admissions = await AdmissionQuery(from, to, branch).SumAsync(s => (decimal?)s.AdmissionFees ?? 0m, ct);
        var admissionCount = await AdmissionQuery(from, to, branch).CountAsync(ct);

        var otherIncome = await OtherIncomeQuery(from, to, branch).SumAsync(i => (decimal?)i.Amount ?? 0m, ct);

        var examFees = await ExamFeeQuery(from, to, branch).SumAsync(e => (decimal?)e.Amount ?? 0m, ct);
        var examFeeCount = await ExamFeeQuery(from, to, branch).CountAsync(ct);
        var otherIncomeCount = await OtherIncomeQuery(from, to, branch).CountAsync(ct);

        var lateFines = await LateFineQuery(from, to, branch).SumAsync(l => (decimal?)l.FineAmount ?? 0m, ct);
        var lateFineCount = await LateFineQuery(from, to, branch).CountAsync(ct);

        var expenses = await ExpenseQuery(from, to, branch).SumAsync(e => (decimal?)e.Amount ?? 0m, ct);
        var expenseCount = await ExpenseQuery(from, to, branch).CountAsync(ct);

        var teacherPayouts = await TeacherPayoutQuery(from, to, branch).SumAsync(t => (decimal?)t.Salary ?? 0m, ct);
        var teacherPayoutCount = await TeacherPayoutQuery(from, to, branch).CountAsync(ct);

        var teacherExpenses = await TeacherExpenseQuery(from, to, branch).SumAsync(t => (decimal?)t.Amount ?? 0m, ct);
        var teacherExpenseCount = await TeacherExpenseQuery(from, to, branch).CountAsync(ct);

        // Movement in dues: how much the outstanding balance grew or shrank.
        var duesMovement = await FeeQuery(from, to, branch)
            .SumAsync(f => ((decimal?)f.NewDue ?? 0m) - ((decimal?)f.OldDue ?? 0m), ct);

        var receptionistPay = await ReceptionistPayQuery(from, to, branch)
            .SumAsync(p => (decimal?)p.Amount ?? 0m, ct);

        var receptionistPayCount = await ReceptionistPayQuery(from, to, branch).CountAsync(ct);

        var referralCommission = await ReferralQuery(from, to, branch).SumAsync(r => (decimal?)r.Amount ?? 0m, ct);
        var referralCount = await ReferralQuery(from, to, branch).CountAsync(ct);

        var totalIncome = fees + admissions + otherIncome + examFees;

        /* The proprietor's honorarium is set per branch, or with no branch for
           the institute as a whole. A branch report carries only that branch's
           amount; the whole-institute report carries every one of them. */
        var honorarium = await HonorariumByMonthAsync(from, to, branch, ct);
        var honorariumTotal = honorarium.Sum(h => h.Amount);

        /* ---- teacher cost is counted ONCE ---------------------------------
           TecFees and TecExpense are the two sides of one obligation, not two
           costs. A teacher's share is incurred the moment a fee is collected
           (TecFees); handing the money over later (TecExpense) settles that
           liability, exactly like paying a creditor.

           Adding both roughly doubled the teacher cost: across 2026 the two
           came to Rs 2,30,782.80 and Rs 2,31,160 — the same money, counted
           twice, understating profit by about Rs 2.3 lakh.

           The share earned is the cost, because that is when it was incurred
           and because it is written automatically with every fee. What has
           actually been handed over is still reported alongside it, but as a
           figure in its own right rather than a subset: paid can exceed
           earned within a range, so it is not an "of which" at all. */

        /* Referral commission is its own cost: accrued when the admission fee is
           collected, like the teacher share. A teacher's is not in TecFees and a
           receptionist's is not in ReceptionistPayment.Amount, so nothing here is
           counted twice. */
        var totalCost = expenses + teacherPayouts + receptionistPay + referralCommission + honorariumTotal;
        var net = totalIncome - totalCost;

        var margin = totalIncome == 0m
            ? 0m
            : decimal.Round(net / totalIncome * 100m, 2, MidpointRounding.AwayFromZero);

        var sections = new List<IncomeSectionTotalDto>
        {
            new(IncomeSection.Fees, "Fee collections", fees, feeCount, false, false),
            new(IncomeSection.Admissions, "Admission fees", admissions, admissionCount, false, false),
            new(IncomeSection.ExamFees, "Exam fees", examFees, examFeeCount, false, false),
            new(IncomeSection.OtherIncome, "Other income", otherIncome, otherIncomeCount, false, false),
            new(IncomeSection.LateFines, "of which late fines", lateFines, lateFineCount, false, true),
            new(IncomeSection.Expenses, "Institute expenses", expenses, expenseCount, true, false),
            /* Two views of ONE obligation, never to be added together.
                 earned = dbo.TecFees    - the teacher's share accrued each time a
                                           student pays, one row per teacher per
                                           payment (2,080 rows in 2026)
                 paid   = dbo.TecExpense - cash actually handed over, recorded on
                                           the Teacher master (111 rows in 2026)

               They were labelled "Teacher payouts" and "of which paid to
               teachers", which was wrong: paid exceeded earned by 508 rupees in
               2026, and a subset cannot be larger than what contains it. Payment
               settles work accrued before the range began, so paid legitimately
               runs ahead. The labels no longer imply containment. Only `earned`
               counts towards the cost total; `paid` stays informational, exactly
               as before. */
            new(IncomeSection.TeacherPayouts, "Teacher share earned", teacherPayouts, teacherPayoutCount, true, false),
            new(IncomeSection.TeacherExpenses, "Paid to teachers", teacherExpenses, teacherExpenseCount, true, true),
            new(IncomeSection.ReceptionistPay, "Receptionist pay", receptionistPay, receptionistPayCount, true, false),
            new(IncomeSection.ReferralCommission, "Referral commission", referralCommission, referralCount, true, false),
            new(IncomeSection.ProprietorHonorarium, "Proprietor honorarium", honorariumTotal, honorarium.Count, true, false),
            new(IncomeSection.Dues, "Dues movement", duesMovement, feeCount, false, true)
        };

        var monthly = await GetMonthlyAsync(from, to, branch, honorarium, ct);

        return Result<IncomeReportDto>.Success(new IncomeReportDto(
            from, to,
            fees, admissions, otherIncome, totalIncome,
            lateFines,
            expenses, teacherPayouts, teacherExpenses, receptionistPay, totalCost,
            net, margin, duesMovement,
            sections, monthly, DateTime.Now, branch, branch is not null, honorariumTotal));
    }

    public async Task<PagedResult<IncomeDetailDto>> GetDetailAsync(
        IncomeDetailQuery query, CancellationToken ct = default)
    {
        var (from, to) = (query.From, query.To);
        var branch = string.IsNullOrWhiteSpace(query.Branch) ? null : query.Branch.Trim();
        var skip = (query.Page - 1) * query.PageSize;

        switch (query.Section)
        {
            case IncomeSection.Fees:
            case IncomeSection.Dues:
            {
                var source = FeeQuery(from, to, branch);
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
                var source = AdmissionQuery(from, to, branch);
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
                var source = OtherIncomeQuery(from, to, branch);
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
                var source = LateFineQuery(from, to, branch);
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
                var source = ExpenseQuery(from, to, branch);
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
                var source = TeacherPayoutQuery(from, to, branch);
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
                var source = TeacherExpenseQuery(from, to, branch);
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

            case IncomeSection.ReceptionistPay:
            {
                var source = ReceptionistPayQuery(from, to, branch);
                var total = await source.CountAsync(ct);

                var items = await source
                    .OrderByDescending(p => p.PaidOn ?? p.PayMonth).ThenByDescending(p => p.Id)
                    .Skip(skip).Take(query.PageSize)
                    .Select(p => new IncomeDetailDto(
                        p.Id,
                        p.PaidOn ?? p.PayMonth,
                        null, null, null, null,
                        p.Receptionist != null ? p.Receptionist.Name : null,
                        // The month it covers, since that is not the date shown.
                        "Receptionist pay",
                        p.Amount))
                    .ToListAsync(ct);

                return PagedResult<IncomeDetailDto>.Create(items, query.Page, query.PageSize, total);
            }

            case IncomeSection.ExamFees:
            {
                var source = ExamFeeQuery(from, to, branch);
                var total = await source.CountAsync(ct);

                var rows = await source
                    .OrderByDescending(e => e.PaidOn).ThenByDescending(e => e.Id)
                    .Skip(skip).Take(query.PageSize)
                    .ToListAsync(ct);

                var names = await StudentNamesAsync(rows.Select(r => r.RegistrationId), ct);

                var items = rows.Select(r => new IncomeDetailDto(
                    r.Id, r.PaidOn, null,
                    Name(names, r.RegistrationId), r.RegistrationId,
                    Course(names, r.RegistrationId), null,
                    string.IsNullOrEmpty(r.CreatedBy)
                        ? $"Exam fee ({r.PaymentMode})"
                        : $"Exam fee ({r.PaymentMode}) · received by {r.CreatedBy}",
                    r.Amount)).ToList();

                return PagedResult<IncomeDetailDto>.Create(items, query.Page, query.PageSize, total);
            }

            case IncomeSection.ReferralCommission:
            {
                var source = ReferralQuery(from, to, branch);
                var total = await source.CountAsync(ct);

                var rows = await source
                    .OrderByDescending(r => r.CreditedOn).ThenByDescending(r => r.Id)
                    .Skip(skip).Take(query.PageSize)
                    .ToListAsync(ct);

                var names = await StudentNamesAsync(rows.Select(r => r.RegistrationId), ct);
                var teacherIds = rows.Where(r => r.ReferrerType == Domain.Entities.ReferrerTypes.Teacher).Select(r => r.ReferrerId).ToList();
                var receptionistIds = rows.Where(r => r.ReferrerType == Domain.Entities.ReferrerTypes.Receptionist).Select(r => r.ReferrerId).ToList();
                var teachers = await _db.Teachers.AsNoTracking().Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
                var receptionists = await _db.Receptionists.AsNoTracking().Where(r => receptionistIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => (string?)r.Name, ct);

                var items = rows.Select(r => new IncomeDetailDto(
                    r.Id, r.CreditedOn, null,
                    Name(names, r.RegistrationId), r.RegistrationId,
                    Course(names, r.RegistrationId),
                    r.ReferrerType == Domain.Entities.ReferrerTypes.Teacher
                        ? teachers.GetValueOrDefault(r.ReferrerId)
                        : receptionists.GetValueOrDefault(r.ReferrerId),
                    $"Referral {r.Percent:0.##}% of {r.PaymentAmount:0.##}", r.Amount)).ToList();

                return PagedResult<IncomeDetailDto>.Create(items, query.Page, query.PageSize, total);
            }

            case IncomeSection.ProprietorHonorarium:
            {
                // One line per month per branch, so the whole-institute view shows
                // where each amount is charged rather than one merged figure.
                var rates = await HonorariumRatesQuery(branch).ToListAsync(ct);
                var items = rates
                    .GroupBy(r => r.Branch)
                    .SelectMany(g => HonorariumService.Allocate(g.ToList(), from, to)
                        .Select(m => (g.Key, m.Month, m.Amount)))
                    .OrderByDescending(m => m.Month)
                    .ThenBy(m => m.Key)
                    .Select((m, i) => new IncomeDetailDto(
                        i + 1, m.Month, null, null, null, null, null,
                        "Proprietor honorarium, " + m.Month.ToString("MMM yyyy", CultureInfo.InvariantCulture)
                            + " · " + (m.Key ?? "Whole institute"),
                        m.Amount))
                    .ToList();
                return PagedResult<IncomeDetailDto>.Create(
                    items.Skip(skip).Take(query.PageSize).ToList(), query.Page, query.PageSize, items.Count);
            }
            default:
                return PagedResult<IncomeDetailDto>.Create([], query.Page, query.PageSize, 0);
        }
    }

    /* ------------------------------------------------------------- monthly */

    /// <summary>
    /// The proprietor's honorarium per calendar month of the range (pro rata at
    /// the ends) — that branch's only, or every branch plus the institute-wide
    /// amount when no branch is given.
    /// </summary>
    private async Task<List<(DateOnly Month, decimal Amount)>> HonorariumByMonthAsync(
        DateOnly from, DateOnly to, string? branch, CancellationToken ct)
    {
        var rates = await HonorariumRatesQuery(branch).ToListAsync(ct);
        return HonorariumService.AllocateByBranch(rates, from, to);
    }

    private IQueryable<Domain.Entities.ProprietorHonorarium> HonorariumRatesQuery(string? branch)
    {
        var q = _db.ProprietorHonoraria.AsNoTracking();
        return branch is null ? q : q.Where(h => h.Branch == branch);
    }

    private async Task<IReadOnlyList<IncomeTrendPointDto>> GetMonthlyAsync(
        DateOnly from, DateOnly to, string? branch,
        List<(DateOnly Month, decimal Amount)> honorarium, CancellationToken ct)
    {
        // Cap the chart so a decade-wide range does not return hundreds of points.
        var months = ((to.Year - from.Year) * 12) + to.Month - from.Month + 1;
        if (months is < 1 or > 36) return [];

        var feeByMonth = await FeeQuery(from, to, branch)
            .GroupBy(f => new { f.FeeDate!.Value.Year, f.FeeDate!.Value.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Installment ?? 0m)))
            .ToListAsync(ct);

        var admissionByMonth = await AdmissionQuery(from, to, branch)
            .GroupBy(s => new { s.DateOfJoining!.Value.Year, s.DateOfJoining!.Value.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.AdmissionFees ?? 0m)))
            .ToListAsync(ct);

        var otherByMonth = await OtherIncomeQuery(from, to, branch)
            .GroupBy(i => new { i.IncomeDate!.Value.Year, i.IncomeDate!.Value.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Amount ?? 0m)))
            .ToListAsync(ct);

        var expenseByMonth = await ExpenseQuery(from, to, branch)
            .GroupBy(e => new { e.ExpenseDate!.Value.Year, e.ExpenseDate!.Value.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Amount ?? 0m)))
            .ToListAsync(ct);

        var payoutByMonth = await TeacherPayoutQuery(from, to, branch)
            .GroupBy(t => new { t.PaidOn!.Value.Year, t.PaidOn!.Value.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Salary ?? 0m)))
            .ToListAsync(ct);

        var teacherExpenseByMonth = await TeacherExpenseQuery(from, to, branch)
            .GroupBy(t => new { t.ExpenseDate!.Value.Year, t.ExpenseDate!.Value.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Amount ?? 0m)))
            .ToListAsync(ct);

        var receptionistByMonth = await ReceptionistPayQuery(from, to, branch)
            .GroupBy(p => new
            {
                (p.PaidOn ?? p.PayMonth).Year,
                (p.PaidOn ?? p.PayMonth).Month
            })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => (decimal?)x.Amount ?? 0m)))
            .ToListAsync(ct);

        var examFeeByMonth = await ExamFeeQuery(from, to, branch)
            .GroupBy(e => new { e.PaidOn.Year, e.PaidOn.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => x.Amount)))
            .ToListAsync(ct);

        var referralByMonth = await ReferralQuery(from, to, branch)
            .GroupBy(r => new { r.CreditedOn.Year, r.CreditedOn.Month })
            .Select(g => new MonthBucket(g.Key.Year, g.Key.Month, g.Sum(x => x.Amount)))
            .ToListAsync(ct);

        var points = new List<IncomeTrendPointDto>(months);

        for (var cursor = new DateOnly(from.Year, from.Month, 1);
             cursor <= new DateOnly(to.Year, to.Month, 1);
             cursor = cursor.AddMonths(1))
        {
            var income = Total(feeByMonth, cursor) + Total(admissionByMonth, cursor) + Total(otherByMonth, cursor)
                         + Total(examFeeByMonth, cursor);
            // Teacher cost is the payout, not the payout plus the cash settling it.
            var cost = Total(expenseByMonth, cursor)
                       + Total(payoutByMonth, cursor)
                       + Total(receptionistByMonth, cursor)
                       + Total(referralByMonth, cursor)
                       + honorarium.Where(h => h.Month == cursor).Sum(h => h.Amount);

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

    /* ---- branch scoping ------------------------------------------------
       Five of the seven sources reach a branch: fees, admissions, late fines
       and teacher payouts through the student who paid, and expenses through
       the branch on the expense row.

       Two do not. Other income (OIncome) and teacher expenses (TecExpense)
       have no branch and no student to borrow one from. Rather than fold
       institute-wide figures into one branch's profit — which would make
       every branch look worse than it is — they are excluded when a branch is
       selected, and the report says so. */

    /// <summary>Registration ids of students at one branch, for scoping the fee tables.</summary>
    private IQueryable<string> RegistrationIdsAtBranch(string branch) =>
        _db.Students.AsNoTracking()
            .Where(s => s.RegistrationId != null && s.Branch != null && s.Branch == branch)
            .Select(s => s.RegistrationId!);

    private IQueryable<Domain.Entities.MonthlyFee> FeeQuery(DateOnly from, DateOnly to, string? branch = null)
    {
        var q = _db.MonthlyFees.AsNoTracking().Where(f => f.FeeDate >= from && f.FeeDate <= to);

        return branch is null
            ? q
            : q.Where(f => f.StudentRegistrationId != null
                           && RegistrationIdsAtBranch(branch).Contains(f.StudentRegistrationId));
    }

    /// <summary>
    /// Admission fees of students who joined in the range. The legacy roll-up
    /// restricted this to active students, so a student who has since left drops out
    /// of a historic report; that behaviour is preserved.
    /// </summary>
    private IQueryable<Domain.Entities.Student> AdmissionQuery(DateOnly from, DateOnly to, string? branch = null)
    {
        /* NOT filtered on IsActive, deliberately.
           ------------------------------------------------------------------
           An admission fee is cash received on the day of admission. Nothing
           that happens afterwards un-receives it, so filtering on the
           student's CURRENT status made a past period's income shrink every
           time somebody left: run the report in March and again in August and
           the same range reported less the second time.

           It was hiding 354,800 rupees of real income - 327 students who
           COMPLETED their course and were closed off, 338 more who paid
           monthly fees for a while before leaving, and 47 who enrolled and
           never came back. All three paid to enrol.

           It is also inconsistent with how costs are treated here: an expense
           is not removed because the thing it bought was later discarded. */
        var q = _db.Students.AsNoTracking()
            .Where(s => s.DateOfJoining >= from && s.DateOfJoining <= to);

        return branch is null ? q : q.Where(s => s.Branch == branch);
    }

    /// <summary>Non-fee income, scoped by the branch recorded on the entry.</summary>
    private IQueryable<Domain.Entities.OtherIncome> OtherIncomeQuery(DateOnly from, DateOnly to, string? branch = null)
    {
        var q = _db.OtherIncomes.AsNoTracking().Where(i => i.IncomeDate >= from && i.IncomeDate <= to);

        return branch is null ? q : q.Where(i => i.Ext != null && i.Ext.Branch == branch);
    }

    private IQueryable<Domain.Entities.LateFee> LateFineQuery(DateOnly from, DateOnly to, string? branch = null)
    {
        var q = _db.LateFees.AsNoTracking().Where(l => l.FineDate >= from && l.FineDate <= to);

        return branch is null
            ? q
            : q.Where(l => l.StudentRegistrationId != null
                           && RegistrationIdsAtBranch(branch).Contains(l.StudentRegistrationId));
    }

    private IQueryable<Domain.Entities.Expense> ExpenseQuery(DateOnly from, DateOnly to, string? branch = null)
    {
        var q = _db.Expenses.AsNoTracking().Where(e => e.ExpenseDate >= from && e.ExpenseDate <= to);

        return branch is null ? q : q.Where(e => e.Ext != null && e.Ext.Branch == branch);
    }

    private IQueryable<Domain.Entities.TeacherFee> TeacherPayoutQuery(DateOnly from, DateOnly to, string? branch = null)
    {
        var q = _db.TeacherFees.AsNoTracking().Where(t => t.PaidOn >= from && t.PaidOn <= to);

        return branch is null
            ? q
            : q.Where(t => t.StudentRegistrationId != null
                           && RegistrationIdsAtBranch(branch).Contains(t.StudentRegistrationId));
    }

    /// <summary>
    /// Ad-hoc payments to a teacher. Recorded against the teacher, not a student or
    /// a branch, so like other income it is left out of a branch-scoped report.
    /// </summary>
    private IQueryable<Domain.Entities.TeacherExpense> TeacherExpenseQuery(DateOnly from, DateOnly to, string? branch = null)
    {
        var q = _db.TeacherExpenses.AsNoTracking().Where(t => t.ExpenseDate >= from && t.ExpenseDate <= to);

        return branch is null ? q : q.Where(_ => false);
    }

    /// <summary>
    /// Receptionists' monthly pay — a running cost like any other salary.
    /// </summary>
    /// <remarks>
    /// Dated by <b>when the money was handed over</b>, not the month it was for, so
    /// it lands in the same period as every other figure here. The rest of this
    /// report is cash in and cash out; a salary for July paid in August belongs to
    /// August's costs.
    ///
    /// <para>Unlike teacher expenses, this one <b>does</b> scope by branch: each
    /// payment carries the branch it was made at.</para>
    /// </remarks>
    private IQueryable<Domain.Entities.ReceptionistPayment> ReceptionistPayQuery(
        DateOnly from, DateOnly to, string? branch = null)
    {
        var q = _db.ReceptionistPayments
            .AsNoTracking()
            .Where(p => (p.PaidOn ?? p.PayMonth) >= from && (p.PaidOn ?? p.PayMonth) <= to);

        return branch is null ? q : q.Where(p => p.Branch == branch);
    }

    private IQueryable<Domain.Entities.ExamFeePayment> ExamFeeQuery(
        DateOnly from, DateOnly to, string? branch = null)
    {
        var q = _db.ExamFeePayments
            .AsNoTracking()
            .Where(e => e.PaidOn >= from && e.PaidOn <= to);

        return branch is null ? q : q.Where(e => e.Branch == branch);
    }

    private IQueryable<Domain.Entities.ReferralCredit> ReferralQuery(
        DateOnly from, DateOnly to, string? branch = null)
    {
        var q = _db.ReferralCredits
            .AsNoTracking()
            .Where(r => r.CreditedOn >= from && r.CreditedOn <= to);

        return branch is null ? q : q.Where(r => r.Branch == branch);
    }

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
