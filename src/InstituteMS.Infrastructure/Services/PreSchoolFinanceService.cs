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

        var rows = staff.Select(s =>
        {
            var p = payments.FirstOrDefault(x => x.StaffId == s.Id);
            return new PreSchoolSalaryRowDto(s.Id, s.Name, s.Role, s.MonthlySalary, p is null ? null : ToDto(p, school));
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

        var payment = new PreSchoolSalaryPayment
        {
            StaffId = s.Id,
            ForMonth = month,
            PaidOn = r.PaidOn ?? DateOnly.FromDateTime(DateTime.Today),
            Salary = s.MonthlySalary,
            DaysAbsent = r.DaysAbsent,
            Deduction = r.Deduction,
            NetPaid = s.MonthlySalary - r.Deduction,
            Mode = r.Mode,
            Reference = r.Mode == "Cash" ? null : Clean(r.Reference),
            Notes = Clean(r.Notes),
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        };
        _db.PreSchoolSalaryPayments.Add(payment);
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

        var lines = await _db.PreSchoolReceiptLines.AsNoTracking()
            .Where(l => !l.Receipt!.IsCancelled && l.Receipt.ReceivedOn >= from && l.Receipt.ReceivedOn <= to)
            .Select(l => new { l.Receipt!.ReceivedOn, l.Kind, l.Amount, l.Receipt.Mode })
            .ToListAsync(ct);
        var salaries = await _db.PreSchoolSalaryPayments.AsNoTracking()
            .Where(p => p.PaidOn >= from && p.PaidOn <= to)
            .Select(p => new { p.PaidOn, p.NetPaid })
            .ToListAsync(ct);
        var expenses = await _db.PreSchoolExpenses.AsNoTracking()
            .Where(e => e.SpentOn >= from && e.SpentOn <= to)
            .Select(e => new { e.SpentOn, e.Head, e.Amount })
            .ToListAsync(ct);

        var months = Enumerable.Range(1, 12).Select(m =>
        {
            var admission = lines.Where(l => l.ReceivedOn.Month == m && l.Kind == "Admission").Sum(l => l.Amount);
            var monthly = lines.Where(l => l.ReceivedOn.Month == m && l.Kind == "Monthly").Sum(l => l.Amount);
            var paid = salaries.Where(p => p.PaidOn.Month == m).Sum(p => p.NetPaid);
            var spent = expenses.Where(e => e.SpentOn.Month == m).Sum(e => e.Amount);
            return new PreSchoolIncomeMonthDto(new DateOnly(year, m, 1), admission, monthly, admission + monthly, paid, spent, admission + monthly - paid - spent);
        }).ToList();

        var income = months.Sum(m => m.Income);
        var salaryTotal = months.Sum(m => m.Salaries);
        var expenseTotal = months.Sum(m => m.Expenses);

        return new PreSchoolIncomeReportDto(
            year, months, income, salaryTotal, expenseTotal, income - salaryTotal - expenseTotal,
            lines.GroupBy(l => l.Mode).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount)),
            expenses.GroupBy(e => e.Head).OrderByDescending(g => g.Sum(e => e.Amount)).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount)));
    }

    public async Task<Result<PreSchoolIncomeRangeDto>> IncomeRangeAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from) return Result<PreSchoolIncomeRangeDto>.Failure("The end date is before the start date.");
        if (to.DayNumber - from.DayNumber > 3700) return Result<PreSchoolIncomeRangeDto>.Failure("Pick at most about ten years at a time.");

        var prefix = (await _db.PreSchoolSettings.AsNoTracking().Select(x => x.ReceiptPrefix).FirstOrDefaultAsync(ct)) ?? "APS";

        var receipts = await _db.PreSchoolReceipts.AsNoTracking()
            .Include(r => r.Lines).Include(r => r.Student).ThenInclude(s => s!.Class)
            .Where(r => !r.IsCancelled && r.ReceivedOn >= from && r.ReceivedOn <= to)
            .OrderBy(r => r.ReceivedOn).ThenBy(r => r.ReceiptNo)
            .ToListAsync(ct);
        var receiptLines = receipts.Select(r => new PreSchoolIncomeReceiptLineDto(
            r.ReceivedOn, $"{prefix}/{r.ReceiptNo:0000}", r.Student?.ChildName ?? "", r.Student?.Class?.Name ?? "", r.Mode,
            r.Lines.Where(l => l.Kind == "Admission").Sum(l => l.Amount),
            r.Lines.Where(l => l.Kind == "Monthly").Sum(l => l.Amount),
            r.Total)).ToList();

        var salaries = await _db.PreSchoolSalaryPayments.AsNoTracking().Include(p => p.Staff)
            .Where(p => p.PaidOn >= from && p.PaidOn <= to)
            .OrderBy(p => p.PaidOn).ThenBy(p => p.Id)
            .Select(p => new PreSchoolIncomeSalaryLineDto(p.PaidOn, p.Staff!.Name, p.Staff.Role, p.ForMonth, p.Mode, p.NetPaid))
            .ToListAsync(ct);

        var expenses = (await ListExpensesAsync(from, to, ct)).OrderBy(e => e.SpentOn).ThenBy(e => e.Id).ToList();

        var admission = receiptLines.Sum(r => r.AdmissionFee);
        var monthly = receiptLines.Sum(r => r.MonthlyFees);
        var income = receiptLines.Sum(r => r.Total);
        var paid = salaries.Sum(s => s.Amount);
        var spent = expenses.Sum(e => e.Amount);

        return Result<PreSchoolIncomeRangeDto>.Success(new PreSchoolIncomeRangeDto(
            from, to, admission, monthly, income, paid, spent, income - paid - spent,
            receiptLines.GroupBy(r => r.Mode).ToDictionary(g => g.Key, g => g.Sum(r => r.Total)),
            expenses.GroupBy(e => e.Head).OrderByDescending(g => g.Sum(e => e.Amount)).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount)),
            receiptLines, salaries, expenses));
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
        p.NetPaid, NumberToWords.Convert(p.NetPaid), p.Mode, p.Reference, p.Notes, p.CreatedBy, school.Name, school.Address);

    private static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
