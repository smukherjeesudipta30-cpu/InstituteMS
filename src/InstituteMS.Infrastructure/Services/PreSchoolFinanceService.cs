using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The PreSchool's staff on fixed monthly salaries, its expenses and its income
/// report. Reads and writes PreSchool* tables only: the Computer Centre's
/// payroll rules (teacher percentage, commission, receptionist bonus) are not
/// used and its expenses and income are not counted.
/// </summary>
public class PreSchoolFinanceService : IPreSchoolFinanceService
{
    private readonly AppDbContext _db;

    public PreSchoolFinanceService(AppDbContext db) => _db = db;

    /* ------------------------------------------------------------ staff */

    public async Task<IReadOnlyList<PreSchoolStaffDto>> ListStaffAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return (await _db.PreSchoolStaff.AsNoTracking().OrderBy(s => s.LeftOn != null).ThenBy(s => s.Role).ThenBy(s => s.Name).ToListAsync(ct))
            .Select(s => ToDto(s, today)).ToList();
    }

    public async Task<Result<PreSchoolStaffDto>> SaveStaffAsync(int? id, PreSchoolStaffSaveRequest r, string? actor, CancellationToken ct = default)
    {
        PreSchoolStaffMember? s;
        if (id is null)
        {
            s = new PreSchoolStaffMember { CreatedBy = actor, CreatedOn = DateTime.Now };
            _db.PreSchoolStaff.Add(s);
        }
        else
        {
            s = await _db.PreSchoolStaff.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Result<PreSchoolStaffDto>.Failure("That person is not on the staff list.", ResultStatus.NotFound);
            s.ModifiedBy = actor;
            s.ModifiedOn = DateTime.Now;
        }

        s.Name = r.Name.Trim();
        s.Role = r.Role;
        s.Phone = Clean(r.Phone);
        s.Email = Clean(r.Email);
        s.JoinedOn = r.JoinedOn;
        s.LeftOn = r.LeftOn;
        s.MonthlySalary = r.MonthlySalary;
        s.Notes = Clean(r.Notes);
        await _db.SaveChangesAsync(ct);

        return Result<PreSchoolStaffDto>.Success(ToDto(s, DateOnly.FromDateTime(DateTime.Today)));
    }

    /* ----------------------------------------------------------- salary */

    public async Task<PreSchoolSalarySheetDto> SalarySheetAsync(DateOnly month, CancellationToken ct = default)
    {
        var first = FirstOfMonth(month);
        var last = first.AddMonths(1).AddDays(-1);
        var school = await SchoolAsync(ct);

        // Everyone who worked at least part of the month, plus anyone already paid for it.
        var payments = await _db.PreSchoolSalaryPayments.AsNoTracking().Include(p => p.Staff)
            .Where(p => p.ForMonth == first).ToListAsync(ct);
        var paidIds = payments.Select(p => p.StaffId).ToList();
        var staff = await _db.PreSchoolStaff.AsNoTracking()
            .Where(s => (s.JoinedOn <= last && (s.LeftOn == null || s.LeftOn >= first)) || paidIds.Contains(s.Id))
            .OrderBy(s => s.Role).ThenBy(s => s.Name).ToListAsync(ct);

        // Reference fees earned by the end of the month and not yet paid out: they go with this salary.
        var pending = (await _db.PreSchoolReferralCredits.AsNoTracking()
                .Where(c => c.SalaryPaymentId == null && c.CreditedOn <= last && !c.ReceiptPayment!.Receipt!.IsCancelled)
                .Select(c => new { c.StaffId, c.Amount }).ToListAsync(ct))
            .GroupBy(c => c.StaffId).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        var rows = staff.Select(s =>
        {
            var p = payments.FirstOrDefault(x => x.StaffId == s.Id);
            return new PreSchoolSalaryRowDto(s.Id, s.Name, s.Role, s.MonthlySalary, p is null ? null : ToDto(p, school),
                p is null ? pending.GetValueOrDefault(s.Id) : 0);
        }).ToList();

        var totalPaid = rows.Sum(r => r.Payment?.NetPaid ?? 0);
        var unpaid = rows.Where(r => r.Payment is null).Sum(r => r.MonthlySalary);
        return new PreSchoolSalarySheetDto(first, rows, rows.Sum(r => r.Payment?.Salary ?? r.MonthlySalary), totalPaid, unpaid);
    }

    public async Task<Result<PreSchoolSalaryPaymentDto>> PaySalaryAsync(PreSchoolSalaryPayRequest r, string? actor, CancellationToken ct = default)
    {
        var s = await _db.PreSchoolStaff.AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.StaffId, ct);
        if (s is null) return Result<PreSchoolSalaryPaymentDto>.Failure("That person is not on the staff list.", ResultStatus.NotFound);

        var month = FirstOfMonth(r.ForMonth);
        if (month < FirstOfMonth(s.JoinedOn))
            return Result<PreSchoolSalaryPaymentDto>.Failure($"{s.Name} had not joined by {month:MMM yyyy}.");
        if (s.LeftOn is DateOnly left && month > FirstOfMonth(left))
            return Result<PreSchoolSalaryPaymentDto>.Failure($"{s.Name} had left before {month:MMM yyyy}.");
        if (month > FirstOfMonth(DateOnly.FromDateTime(DateTime.Today)))
            return Result<PreSchoolSalaryPaymentDto>.Failure("A salary cannot be paid for a month that has not started.");
        if (await _db.PreSchoolSalaryPayments.AnyAsync(p => p.StaffId == s.Id && p.ForMonth == month, ct))
            return Result<PreSchoolSalaryPaymentDto>.Failure($"{s.Name} has already been paid for {month:MMM yyyy}.", ResultStatus.Conflict);
        if (r.Deduction > s.MonthlySalary)
            return Result<PreSchoolSalaryPaymentDto>.Failure("The deduction cannot be more than the salary.");

        // Reference fees earned up to the end of this month and not yet paid out are paid with it.
        var monthEnd = month.AddMonths(1).AddDays(-1);
        var credits = await _db.PreSchoolReferralCredits
            .Where(c => c.StaffId == s.Id && c.SalaryPaymentId == null && c.CreditedOn <= monthEnd && !c.ReceiptPayment!.Receipt!.IsCancelled)
            .ToListAsync(ct);
        var bonus = credits.Sum(c => c.Amount);

        var payment = new PreSchoolSalaryPayment
        {
            StaffId = s.Id,
            ForMonth = month,
            PaidOn = r.PaidOn ?? DateOnly.FromDateTime(DateTime.Today),
            Salary = s.MonthlySalary,
            DaysAbsent = r.DaysAbsent,
            Deduction = r.Deduction,
            ReferralBonus = bonus,
            NetPaid = s.MonthlySalary - r.Deduction + bonus,
            Mode = r.Mode,
            Reference = r.Mode == "Cash" ? null : Clean(r.Reference),
            Notes = Clean(r.Notes),
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        };
        _db.PreSchoolSalaryPayments.Add(payment);
        foreach (var c in credits) c.SalaryPayment = payment;
        await _db.SaveChangesAsync(ct);

        return await GetSalaryPaymentAsync(payment.Id, ct);
    }

    public async Task<Result<PreSchoolSalaryPaymentDto>> GetSalaryPaymentAsync(int id, CancellationToken ct = default)
    {
        var p = await _db.PreSchoolSalaryPayments.AsNoTracking().Include(x => x.Staff).FirstOrDefaultAsync(x => x.Id == id, ct);
        return p is null
            ? Result<PreSchoolSalaryPaymentDto>.Failure("That salary payment does not exist.", ResultStatus.NotFound)
            : Result<PreSchoolSalaryPaymentDto>.Success(ToDto(p, await SchoolAsync(ct)));
    }

    public async Task<Result<bool>> DeleteSalaryPaymentAsync(int id, CancellationToken ct = default)
    {
        await _db.PreSchoolReferralCredits.Where(c => c.SalaryPaymentId == id)
            .ExecuteUpdateAsync(u => u.SetProperty(c => c.SalaryPaymentId, (int?)null), ct);
        var removed = await _db.PreSchoolSalaryPayments.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        return removed == 0
            ? Result<bool>.Failure("That salary payment does not exist.", ResultStatus.NotFound)
            : Result<bool>.Success(true);
    }

    /* --------------------------------------------------------- expenses */

    public async Task<IReadOnlyList<PreSchoolExpenseDto>> ListExpensesAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await _db.PreSchoolExpenses.AsNoTracking()
            .Where(e => e.SpentOn >= from && e.SpentOn <= to)
            .OrderByDescending(e => e.SpentOn).ThenByDescending(e => e.Id)
            .Select(e => new PreSchoolExpenseDto(e.Id, e.SpentOn, e.Head, e.Description, e.Amount, e.Mode, e.Reference, e.CreatedBy))
            .ToListAsync(ct);

    public async Task<Result<PreSchoolExpenseDto>> SaveExpenseAsync(int? id, PreSchoolExpenseSaveRequest r, string? actor, CancellationToken ct = default)
    {
        PreSchoolExpense? e;
        if (id is null)
        {
            e = new PreSchoolExpense { CreatedBy = actor, CreatedOn = DateTime.Now };
            _db.PreSchoolExpenses.Add(e);
        }
        else
        {
            e = await _db.PreSchoolExpenses.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return Result<PreSchoolExpenseDto>.Failure("That expense does not exist.", ResultStatus.NotFound);
            e.ModifiedBy = actor;
            e.ModifiedOn = DateTime.Now;
        }

        e.SpentOn = r.SpentOn;
        e.Head = r.Head.Trim();
        e.Description = Clean(r.Description);
        e.Amount = r.Amount;
        e.Mode = r.Mode;
        e.Reference = r.Mode == "Cash" ? null : Clean(r.Reference);
        await _db.SaveChangesAsync(ct);

        return Result<PreSchoolExpenseDto>.Success(new PreSchoolExpenseDto(e.Id, e.SpentOn, e.Head, e.Description, e.Amount, e.Mode, e.Reference, e.CreatedBy));
    }

    public async Task<Result<bool>> DeleteExpenseAsync(int id, CancellationToken ct = default)
    {
        var removed = await _db.PreSchoolExpenses.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
        return removed == 0
            ? Result<bool>.Failure("That expense does not exist.", ResultStatus.NotFound)
            : Result<bool>.Success(true);
    }

    /* ----------------------------------------------------------- income */

    public async Task<PreSchoolIncomeReportDto> IncomeAsync(int year, CancellationToken ct = default)
    {
        var from = new DateOnly(year, 1, 1);
        var to = new DateOnly(year, 12, 31);

        var lines = await CollectionsAsync(from, to, ct);
        var salaries = await _db.PreSchoolSalaryPayments.AsNoTracking()
            .Where(p => p.PaidOn >= from && p.PaidOn <= to)
            .Select(p => new { p.PaidOn, p.NetPaid, p.ReferralBonus })
            .ToListAsync(ct);
        var expenses = await _db.PreSchoolExpenses.AsNoTracking()
            .Where(e => e.SpentOn >= from && e.SpentOn <= to)
            .Select(e => new { e.SpentOn, e.Head, e.Amount })
            .ToListAsync(ct);
        // Store stock is a cost when My Chhota School is paid (in bulk), not when it arrives.
        var purchases = await _db.PreSchoolSupplierPayments.AsNoTracking()
            .Where(p => p.PaidOn >= from && p.PaidOn <= to)
            .Select(p => new { OnDate = p.PaidOn, p.Amount })
            .ToListAsync(ct);

        var months = Enumerable.Range(1, 12).Select(m =>
        {
            var inMonth = lines.Where(l => l.ReceivedOn.Month == m).ToList();
            var admission = inMonth.Sum(l => l.AdmissionFee);
            var monthly = inMonth.Sum(l => l.MonthlyFees);
            var other = inMonth.Sum(l => l.OtherFees);
            var discounts = inMonth.Sum(l => l.Discount);
            var paid = salaries.Where(p => p.PaidOn.Month == m).Sum(p => p.NetPaid - p.ReferralBonus);
            var referrals = salaries.Where(p => p.PaidOn.Month == m).Sum(p => p.ReferralBonus);
            var spent = expenses.Where(e => e.SpentOn.Month == m).Sum(e => e.Amount);
            var bought = purchases.Where(p => p.OnDate.Month == m).Sum(p => p.Amount);
            var fees = admission + monthly + other - discounts;
            return new PreSchoolIncomeMonthDto(new DateOnly(year, m, 1), admission, monthly, fees, paid, spent,
                fees - paid - referrals - spent - bought, other, bought, discounts, referrals);
        }).ToList();

        var income = months.Sum(m => m.Income);
        var salaryTotal = months.Sum(m => m.Salaries);
        var expenseTotal = months.Sum(m => m.Expenses);
        var purchaseTotal = months.Sum(m => m.Purchases);
        var referralTotal = months.Sum(m => m.Referrals);

        return new PreSchoolIncomeReportDto(
            year, months, income, salaryTotal, expenseTotal, income - salaryTotal - referralTotal - expenseTotal - purchaseTotal,
            lines.GroupBy(l => l.Mode).ToDictionary(g => g.Key, g => g.Sum(l => l.Total)),
            expenses.GroupBy(e => e.Head).OrderByDescending(g => g.Sum(e => e.Amount)).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount)),
            purchaseTotal, ByCollector(lines), referralTotal);
    }

    private static Dictionary<string, decimal> ByCollector(IEnumerable<PreSchoolIncomeReceiptLineDto> lines) =>
        lines.GroupBy(l => string.IsNullOrWhiteSpace(l.CollectedBy) ? "(not recorded)" : l.CollectedBy!)
            .OrderByDescending(g => g.Sum(l => l.Total))
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Total));

    /// <summary>
    /// Every payment received in the dates (a whole receipt, or one instalment
    /// of it), on the day it was paid. Each carries its share of the receipt's
    /// admission, monthly, other and discount rows, so a receipt paid in parts
    /// adds up to the receipt once fully paid. Cancelled receipts count for nothing.
    /// </summary>
    private async Task<List<PreSchoolIncomeReceiptLineDto>> CollectionsAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var prefix = (await _db.PreSchoolSettings.AsNoTracking().Select(x => x.ReceiptPrefix).FirstOrDefaultAsync(ct)) ?? "APS";
        var payments = await _db.PreSchoolReceiptPayments.AsNoTracking()
            .Include(p => p.Receipt!).ThenInclude(r => r.Lines)
            .Include(p => p.Receipt!).ThenInclude(r => r.Student!).ThenInclude(s => s.Class)
            .Where(p => !p.Receipt!.IsCancelled && p.PaidOn >= from && p.PaidOn <= to)
            .OrderBy(p => p.PaidOn).ThenBy(p => p.Receipt!.ReceiptNo).ThenBy(p => p.InstalmentNo)
            .ToListAsync(ct);

        return payments.Select(p =>
        {
            var r = p.Receipt!;
            var share = r.Total == 0 ? 1m : p.Amount / r.Total;
            decimal Part(string kind) => Math.Round(r.Lines.Where(l => l.Kind == kind).Sum(l => l.Amount) * share, 2);
            return new PreSchoolIncomeReceiptLineDto(
                p.PaidOn, $"{prefix}/{r.ReceiptNo:0000}", r.Student?.ChildName ?? "", r.Student?.Class?.Name ?? "", p.Mode,
                Part("Admission"), Part("Monthly"), p.Amount, Part("Charge"), -Part("Discount"),
                p.CollectedBy, p.InstalmentNo, r.Total,
                r.Student is null ? null : $"{prefix}-{r.Student.AdmissionNo:0000}", r.Student?.ParentName, r.Student?.Phone,
                r.Student?.JoinedOn, r.Student?.Section, p.Reference, r.StudentId);
        }).ToList();
    }

    public async Task<Result<PreSchoolIncomeRangeDto>> IncomeRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) return Result<PreSchoolIncomeRangeDto>.Failure("The end date is before the start date.");
        if (to.DayNumber - from.DayNumber > 3700) return Result<PreSchoolIncomeRangeDto>.Failure("Pick at most about ten years at a time.");

        // Receipts written in the dates (what parents were billed for store items), and the money received in them.
        var receipts = await _db.PreSchoolReceipts.AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => !r.IsCancelled && r.ReceivedOn >= from && r.ReceivedOn <= to)
            .ToListAsync(ct);
        var receiptLines = await CollectionsAsync(from, to, ct);

        var salaries = await _db.PreSchoolSalaryPayments.AsNoTracking().Include(p => p.Staff)
            .Where(p => p.PaidOn >= from && p.PaidOn <= to)
            .OrderBy(p => p.PaidOn).ThenBy(p => p.Id)
            .Select(p => new PreSchoolIncomeSalaryLineDto(p.PaidOn, p.Staff!.Name, p.Staff.Role, p.ForMonth, p.Mode, p.NetPaid, p.ReferralBonus))
            .ToListAsync(ct);

        var expenses = (await ListExpensesAsync(from, to, ct)).OrderBy(e => e.SpentOn).ThenBy(e => e.Id).ToList();

        var admission = receiptLines.Sum(r => r.AdmissionFee);
        var monthly = receiptLines.Sum(r => r.MonthlyFees);
        var income = receiptLines.Sum(r => r.Total);
        var referralsPaid = salaries.Sum(s => s.ReferralBonus);
        var paid = salaries.Sum(s => s.Amount) - referralsPaid;
        var spent = expenses.Sum(e => e.Amount);

        // Store stock bought in these dates, and per kind of item what parents paid for it against what it cost.
        var deliveries = await _db.PreSchoolStoreMovements.AsNoTracking().Include(m => m.Item).ThenInclude(i => i!.Class)
            .Where(m => m.Kind == "In" && m.OnDate >= from && m.OnDate <= to)
            .OrderBy(m => m.OnDate).ThenBy(m => m.Id)
            .ToListAsync(ct);
        var purchaseLines = deliveries.Where(m => m.UnitCost != null)
            .Select(m => new PreSchoolIncomePurchaseLineDto(m.OnDate, m.Item?.Name ?? "", m.Item?.Class?.Name ?? "All classes",
                m.Supplier ?? "My Chhota School", m.Quantity, m.UnitCost!.Value, m.Quantity * m.UnitCost.Value))
            .ToList();
        var received = purchaseLines.Sum(p => p.Amount);
        var supplierPayments = await _db.PreSchoolSupplierPayments.AsNoTracking()
            .Where(p => p.PaidOn >= from && p.PaidOn <= to)
            .OrderBy(p => p.PaidOn).ThenBy(p => p.Id)
            .Select(p => new PreSchoolSupplierPaymentDto(p.Id, p.Supplier, p.PaidOn, p.Amount, p.Mode, p.Reference, p.Note, p.CreatedBy))
            .ToListAsync(ct);
        var bought = supplierPayments.Sum(p => p.Amount);
        var everBought = await _db.PreSchoolStoreMovements.AsNoTracking()
            .Where(m => m.Kind == "In" && m.UnitCost != null && m.OnDate <= to)
            .SumAsync(m => (decimal?)(m.Quantity * m.UnitCost!.Value), ct) ?? 0m;
        var everPaid = await _db.PreSchoolSupplierPayments.AsNoTracking()
            .Where(p => p.PaidOn <= to).SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var given = await _db.PreSchoolStoreMovements.AsNoTracking().Include(m => m.Item)
            .Where(m => m.Kind == "Out" && m.OnDate >= from && m.OnDate <= to)
            .Select(m => new { m.Item!.Name, m.Item.ChargeHead, m.Quantity })
            .ToListAsync(ct);
        var charged = receipts.SelectMany(r => r.Lines).Where(l => l.Kind == "Charge" && l.ChargeHead != null)
            .GroupBy(l => l.ChargeHead!).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount));
        var margins = deliveries.Select(m => new { m.Item!.Name, m.Item.ChargeHead })
            .Concat(given.Select(g => new { g.Name, g.ChargeHead }))
            .GroupBy(x => x.Name).Select(g => g.First())
            .Select(x =>
            {
                var fromParents = x.ChargeHead is null ? 0m : charged.GetValueOrDefault(x.ChargeHead);
                var cost = purchaseLines.Where(p => p.ItemName == x.Name).Sum(p => p.Amount);
                return new PreSchoolStoreMarginDto(x.Name, x.ChargeHead, fromParents, cost, fromParents - cost,
                    deliveries.Where(m => m.Item!.Name == x.Name).Sum(m => m.Quantity),
                    given.Where(g => g.Name == x.Name).Sum(g => g.Quantity));
            })
            .OrderBy(x => x.Item)
            .ToList();

        return Result<PreSchoolIncomeRangeDto>.Success(new PreSchoolIncomeRangeDto(
            from, to, admission, monthly, income, paid, spent, income - paid - referralsPaid - spent - bought,
            receiptLines.GroupBy(r => r.Mode).ToDictionary(g => g.Key, g => g.Sum(r => r.Total)),
            expenses.GroupBy(e => e.Head).OrderByDescending(g => g.Sum(e => e.Amount)).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount)),
            receiptLines, salaries, expenses, receiptLines.Sum(r => r.OtherFees), bought, purchaseLines, margins,
            supplierPayments, Math.Round(everBought - everPaid, 2), Math.Round(received, 2), receiptLines.Sum(r => r.Discount),
            ByCollector(receiptLines), referralsPaid));
    }

    /* ------------------------------------------------------ own payslips */

    public async Task<IReadOnlyList<PreSchoolSalaryPaymentDto>> MyPayslipsAsync(string email, CancellationToken ct = default)
    {
        var me = email.Trim();
        var school = await SchoolAsync(ct);
        var payments = await _db.PreSchoolSalaryPayments.AsNoTracking().Include(p => p.Staff)
            .Where(p => p.Staff!.Email == me)
            .OrderByDescending(p => p.ForMonth)
            .ToListAsync(ct);
        return payments.Select(p => ToDto(p, school)).ToList();
    }

    public async Task<PreSchoolMyReferralsDto> MyReferralsAsync(string email, CancellationToken ct = default)
    {
        var me = email.Trim();
        var credits = await ReferralCreditsAsync(c => c.Staff!.Email == me, ct);
        return new PreSchoolMyReferralsDto(
            credits.Where(c => c.PaidWithSalaryFor is null).Sum(c => c.Amount),
            credits.Where(c => c.PaidWithSalaryFor is not null).Sum(c => c.Amount),
            credits);
    }

    private async Task<List<PreSchoolReferralCreditDto>> ReferralCreditsAsync(
        System.Linq.Expressions.Expression<Func<PreSchoolReferralCredit, bool>> which, CancellationToken ct)
    {
        var prefix = (await _db.PreSchoolSettings.AsNoTracking().Select(x => x.ReceiptPrefix).FirstOrDefaultAsync(ct)) ?? "APS";
        var rows = await _db.PreSchoolReferralCredits.AsNoTracking()
            .Where(which)
            .Where(c => !c.ReceiptPayment!.Receipt!.IsCancelled || c.SalaryPaymentId != null)
            .OrderByDescending(c => c.CreditedOn).ThenByDescending(c => c.Id)
            .Select(c => new
            {
                c.Id, c.CreditedOn, c.Student!.ChildName, c.Student.AdmissionNo, c.ReceiptPayment!.Receipt!.ReceiptNo,
                c.ReceiptPayment.InstalmentNo, c.PaymentAmount, c.Percent, c.Amount, StaffName = c.Staff!.Name,
                PaidFor = c.SalaryPayment == null ? (DateOnly?)null : c.SalaryPayment.ForMonth
            })
            .ToListAsync(ct);
        return rows.Select(c => new PreSchoolReferralCreditDto(
            c.Id, c.CreditedOn, c.ChildName, $"{prefix}-{c.AdmissionNo:0000}", $"{prefix}/{c.ReceiptNo:0000}", c.InstalmentNo,
            c.PaymentAmount, c.Percent, c.Amount, c.StaffName, c.PaidFor)).ToList();
    }

    public async Task<Result<PreSchoolHonorariumReportDto>> HonorariumReportAsync(DateOnly from, DateOnly to, string basis, CancellationToken ct = default)
    {
        if (to < from) return Result<PreSchoolHonorariumReportDto>.Failure("The end date is before the start date.");
        if (to.DayNumber - from.DayNumber > 3700) return Result<PreSchoolHonorariumReportDto>.Failure("Pick at most about ten years at a time.");

        var byMonth = !string.Equals(basis, "paid", StringComparison.OrdinalIgnoreCase);
        // By month: every month the dates touch, each from its 1st to its last day.
        if (byMonth)
        {
            from = FirstOfMonth(from);
            to = FirstOfMonth(to).AddMonths(1).AddDays(-1);
        }
        var firstMonth = FirstOfMonth(from);
        var lastMonth = FirstOfMonth(to);

        var school = await SchoolAsync(ct);
        var query = _db.PreSchoolSalaryPayments.AsNoTracking().Include(p => p.Staff).AsQueryable();
        query = byMonth
            ? query.Where(p => p.ForMonth >= firstMonth && p.ForMonth <= lastMonth)
            : query.Where(p => p.PaidOn >= from && p.PaidOn <= to);
        var payments = (await query.OrderBy(p => p.ForMonth).ThenBy(p => p.Staff!.Role).ThenBy(p => p.Staff!.Name).ToListAsync(ct))
            .Select(p => ToDto(p, school)).ToList();

        var credits = await ReferralCreditsAsync(c => c.CreditedOn >= from && c.CreditedOn <= to, ct);
        var creditStaff = await _db.PreSchoolReferralCredits.AsNoTracking()
            .Where(c => c.CreditedOn >= from && c.CreditedOn <= to)
            .Select(c => new { c.StaffId, c.Amount, c.Staff!.Name, c.Staff.Role }).ToListAsync(ct);

        var byStaff = payments.Select(p => (p.StaffId, p.StaffName, p.Role))
            .Concat(creditStaff.Select(c => (c.StaffId, StaffName: c.Name, c.Role)))
            .Distinct()
            .Select(s =>
            {
                var mine = payments.Where(p => p.StaffId == s.StaffId).ToList();
                return new PreSchoolHonorariumStaffDto(s.StaffId, s.StaffName, s.Role, mine.Count,
                    mine.Sum(p => p.Salary), mine.Sum(p => p.Deduction), mine.Sum(p => p.ReferralBonus), mine.Sum(p => p.NetPaid),
                    creditStaff.Where(c => c.StaffId == s.StaffId).Sum(c => c.Amount));
            })
            .OrderBy(s => s.Role).ThenBy(s => s.Name)
            .ToList();

        return Result<PreSchoolHonorariumReportDto>.Success(new PreSchoolHonorariumReportDto(
            from, to, byMonth ? "month" : "paid",
            payments.Sum(p => p.Salary), payments.Sum(p => p.Deduction), payments.Sum(p => p.ReferralBonus), payments.Sum(p => p.NetPaid),
            byStaff, payments, credits));
    }

    public async Task<Result<PreSchoolCollectionReportDto>> CollectionReportAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) return Result<PreSchoolCollectionReportDto>.Failure("The end date is before the start date.");
        if (to.DayNumber - from.DayNumber > 3700) return Result<PreSchoolCollectionReportDto>.Failure("Pick at most about ten years at a time.");

        var rows = await CollectionsAsync(from, to, ct);
        return Result<PreSchoolCollectionReportDto>.Success(new PreSchoolCollectionReportDto(
            from, to, rows.Sum(r => r.Total), rows.Count, rows,
            rows.GroupBy(r => r.Mode).OrderByDescending(g => g.Sum(r => r.Total)).ToDictionary(g => g.Key, g => g.Sum(r => r.Total)),
            ByCollector(rows),
            rows.GroupBy(r => r.ClassName).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Sum(r => r.Total)),
            rows.GroupBy(r => r.ReceivedOn).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Sum(r => r.Total)),
            rows.Sum(r => r.AdmissionFee), rows.Sum(r => r.MonthlyFees), rows.Sum(r => r.OtherFees), rows.Sum(r => r.Discount)));
    }

    /* ---------------------------------------------------------- helpers */

    private async Task<(string Name, string? Address)> SchoolAsync(CancellationToken ct)
    {
        var s = await _db.PreSchoolSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return (s?.SchoolName ?? "Avijan PreSchool", s?.Address);
    }

    private static PreSchoolStaffDto ToDto(PreSchoolStaffMember s, DateOnly today) => new(
        s.Id, s.Name, s.Role, s.Phone, s.Email, s.JoinedOn, s.LeftOn, s.MonthlySalary, s.Notes, s.LeftOn is null || s.LeftOn >= today);

    private static PreSchoolSalaryPaymentDto ToDto(PreSchoolSalaryPayment p, (string Name, string? Address) school) => new(
        p.Id, p.StaffId, p.Staff?.Name ?? "", p.Staff?.Role ?? "", p.ForMonth, p.PaidOn, p.Salary, p.DaysAbsent, p.Deduction,
        p.NetPaid, NumberToWords.Convert(p.NetPaid), p.Mode, p.Reference, p.Notes, p.CreatedBy, school.Name, school.Address, p.ReferralBonus);

    private static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
