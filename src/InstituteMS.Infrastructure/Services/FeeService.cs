using InstituteMS.Application.Common;
using InstituteMS.Application.Fees;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Monthly course fees. Replaces <c>MonthlyCourseFeesController</c> plus the
/// <c>MonthlyBill</c> / <c>Pending</c> / <c>Due</c> / <c>MonthlyDetail</c> models
/// (MIGRATION_PLAN §4.3, §5.5).
///
/// Recording a payment touches four tables — the legacy <c>usp_SaveData</c> did the
/// same. It is done here in EF Core inside one transaction rather than through the
/// stored procedure, because the SP declares its money parameters as bare
/// <c>decimal</c>, which SQL Server reads as <c>decimal(18,0)</c> and silently
/// rounds every amount to whole rupees.
/// </summary>
public class FeeService : IFeeService
{
    private readonly AppDbContext _db;
    private readonly ILogger<FeeService> _logger;

    public FeeService(AppDbContext db, ILogger<FeeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /* ------------------------------------------------------------------ read */

    public async Task<Result<StudentFeeBillDto>> GetBillAsync(
        string registrationId, CancellationToken ct = default)
    {
        var reg = registrationId.Trim();

        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == reg && s.IsActive)
            .OrderByDescending(s => s.Id)
            .Select(s => new
            {
                s.Id, s.RegistrationId, s.Name, s.AdmissionFees, s.MonthlyFees,
                s.DateOfJoining, s.TechniqueCount,
                s.PrimaryTeacherId, PrimaryTeacher = s.PrimaryTeacher != null ? s.PrimaryTeacher.Name : null,
                s.SecondTeacherId, SecondTeacher = s.SecondTeacher != null ? s.SecondTeacher.Name : null,
                s.ThirdTeacherId, ThirdTeacher = s.ThirdTeacher != null ? s.ThirdTeacher.Name : null,
                CourseName = s.Course != null ? s.Course.Name : null,
                CourseDuration = s.Course != null ? s.Course.Duration : null
            })
            .FirstOrDefaultAsync(ct);

        if (student is null)
        {
            return Result<StudentFeeBillDto>.Failure(
                $"No active student found with registration id '{reg}'.", ResultStatus.NotFound);
        }

        var installmentsPaid = await _db.MonthlyFees.CountAsync(f => f.StudentRegistrationId == reg, ct);
        var lastPayment = await GetLastPaymentDateAsync(reg, ct);
        var outstandingDue = await GetOutstandingDueAsync(reg, ct);

        return Result<StudentFeeBillDto>.Success(new StudentFeeBillDto(
            student.Id, student.RegistrationId!, student.Name,
            student.CourseName, student.CourseDuration,
            student.AdmissionFees, student.MonthlyFees, student.DateOfJoining,
            student.TechniqueCount,
            student.PrimaryTeacherId, student.PrimaryTeacher,
            student.SecondTeacherId, student.SecondTeacher,
            student.ThirdTeacherId, student.ThirdTeacher,
            installmentsPaid, lastPayment, outstandingDue));
    }

    public async Task<PagedResult<FeePaymentDto>> GetPaymentsAsync(
        string registrationId, PagedQuery query, CancellationToken ct = default)
    {
        var reg = registrationId.Trim();

        var payments = _db.MonthlyFees.AsNoTracking().Where(f => f.StudentRegistrationId == reg);

        var totalCount = await payments.CountAsync(ct);

        var rows = await payments
            .OrderByDescending(f => f.FeeDate)
            .ThenByDescending(f => f.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(f => new
            {
                f.Id, f.StudentRegistrationId, f.BillNo, f.FeeDate, f.PaymentDate,
                f.Installment, f.OtherInstallment, f.Percentage, f.OldDue, f.NewDue
            })
            .ToListAsync(ct);

        var billNos = rows.Select(r => r.BillNo).Where(b => b != null).ToList();

        // Late fines live in their own table, keyed by bill number.
        var fines = await _db.LateFees
            .AsNoTracking()
            .Where(l => l.BillNo != null && billNos.Contains(l.BillNo))
            .Select(l => new { l.BillNo, l.FineAmount })
            .ToListAsync(ct);

        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == reg)
            .Select(s => new { s.Name, CourseName = s.Course != null ? s.Course.Name : null })
            .FirstOrDefaultAsync(ct);

        var items = rows
            .Select(r => new FeePaymentDto(
                r.Id, r.StudentRegistrationId, student?.Name, student?.CourseName, r.BillNo,
                r.FeeDate, r.PaymentDate, r.Installment, r.OtherInstallment, r.Percentage,
                r.OldDue, r.NewDue,
                fines.FirstOrDefault(f => f.BillNo == r.BillNo)?.FineAmount))
            .ToList();

        return PagedResult<FeePaymentDto>.Create(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<Result<FeeCalculationDto>> CalculateAsync(
        FeeCalculationRequest request, CancellationToken ct = default)
    {
        var bill = await GetBillAsync(request.RegistrationId, ct);
        if (!bill.Succeeded)
            return Result<FeeCalculationDto>.Failure(bill.Error!, bill.Status);

        var student = bill.Value!;

        var fine = LateFineCalculator.Calculate(
            request.FeeDate, student.DateOfJoining, student.LastPaymentDate, student.InstallmentsPaid);

        var total = request.InstallmentAmount + fine.LateFine;
        var paid = request.AmountPaid ?? 0m;
        var newDue = total + student.OutstandingDue - paid;

        return Result<FeeCalculationDto>.Success(new FeeCalculationDto(
            fine.DaysSincePrevious,
            fine.DaysPerInstallment,
            fine.LateFine,
            total,
            student.OutstandingDue,
            decimal.Round(newDue, 2),
            fine.ReferenceDate,
            fine.Explanation));
    }

    public async Task<Result<FeeReceiptDto>> GetReceiptAsync(int id, CancellationToken ct = default)
    {
        var payment = await _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => new
            {
                f.Id, f.StudentRegistrationId, f.BillNo, f.FeeDate,
                f.OtherInstallment, f.OldDue, f.NewDue
            })
            .FirstOrDefaultAsync(ct);

        if (payment is null)
            return Result<FeeReceiptDto>.Failure("Payment not found.", ResultStatus.NotFound);

        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == payment.StudentRegistrationId)
            .Select(s => new
            {
                s.Name,
                CourseName = s.Course != null ? s.Course.Name : null
            })
            .FirstOrDefaultAsync(ct);

        var nextDue = payment.FeeDate is null
            ? (DateOnly?)null
            : LateFineCalculator.NextDueDate(payment.FeeDate.Value);

        return Result<FeeReceiptDto>.Success(new FeeReceiptDto(
            payment.Id, student?.Name, payment.StudentRegistrationId, student?.CourseName,
            payment.BillNo, payment.OtherInstallment,
            NumberToWords.Convert(payment.OtherInstallment ?? 0m),
            payment.OldDue, payment.NewDue, payment.FeeDate, nextDue));
    }

    /* ----------------------------------------------------------------- write */

    public async Task<Result<FeeRecordedDto>> RecordPaymentAsync(
        FeeSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var reg = request.RegistrationId.Trim();

        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == reg && s.IsActive)
            .OrderByDescending(s => s.Id)
            .Select(s => new
            {
                s.TechniqueCount, s.MonthlyFees,
                s.PrimaryTeacherId, s.SecondTeacherId, s.ThirdTeacherId
            })
            .FirstOrDefaultAsync(ct);

        if (student is null)
            return Result<FeeRecordedDto>.Failure($"No active student found with registration id '{reg}'.", ResultStatus.NotFound);

        var teacherIds = new[] { student.PrimaryTeacherId, student.SecondTeacherId, student.ThirdTeacherId }
            .Where(id => id is > 0).Select(id => id!.Value).Distinct().ToList();

        var teacherCount = student.TechniqueCount is > 0 ? student.TechniqueCount.Value : Math.Max(1, teacherIds.Count);

        var salaryEach = LateFineCalculator.TeacherSalaryEach(
            request.InstallmentAmount, request.TeacherPercentage, teacherCount);

        var strategy = _db.Database.CreateExecutionStrategy();

        var recorded = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var billNo = await NextBillNumberAsync(ct);

            // 1. the fee ledger row
            var fee = new MonthlyFee
            {
                StudentRegistrationId = reg,
                BillNo = billNo,
                FeeDate = request.FeeDate,
                PaymentDate = request.PreviousPaymentDate,
                Installment = request.TotalForMonth,   // legacy stores installment + fine here
                // Pin the agreed fee onto the row, so a later fee change cannot make
                // this payment unreadable (db/migrations/010).
                AgreedMonthlyFee = request.InstallmentAmount,
                OtherInstallment = request.AmountPaid,
                Percentage = request.TeacherPercentage,
                OldDue = request.OldDue,
                NewDue = request.NewDue,
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow
            };
            _db.MonthlyFees.Add(fee);

            // 2. the income roll-up the reports read
            _db.FeeTotals.Add(new FeeTotal
            {
                StudentRegistrationId = reg,
                Total = request.TotalForMonth,
                StoreDate = request.FeeDate,
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow
            });

            // 3. the fine, always written so the late-fine report stays complete
            _db.LateFees.Add(new LateFee
            {
                StudentRegistrationId = reg,
                BillNo = billNo,
                FineAmount = request.LateFine,
                FineDate = request.FeeDate,
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow
            });

            // 4. one payout row per assigned teacher
            foreach (var teacherId in teacherIds)
            {
                _db.TeacherFees.Add(new TeacherFee
                {
                    TeacherId = teacherId,
                    StudentRegistrationId = reg,
                    BillNo = billNo,
                    Salary = salaryEach,
                    PaidOn = request.FeeDate,
                    CreatedBy = actor,
                    CreatedOn = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new FeeRecordedDto(fee.Id, billNo, salaryEach);
        });

        _logger.LogInformation(
            "Fee {BillNo} recorded for {RegistrationId}: total {Total}, paid {Paid}, fine {Fine}, {Teachers} teacher payout(s) of {Each} by {Actor}",
            recorded.BillNo, reg, request.TotalForMonth, request.AmountPaid, request.LateFine,
            teacherIds.Count, salaryEach, actor);

        return Result<FeeRecordedDto>.Success(recorded);
    }

    public async Task<Result<bool>> UpdatePaymentAsync(
        int id, FeeSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var fee = await _db.MonthlyFees.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fee is null)
            return Result<bool>.Failure("Payment not found.", ResultStatus.NotFound);

        var reg = request.RegistrationId.Trim();
        var billNo = fee.BillNo;

        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == reg)
            .OrderByDescending(s => s.Id)
            .Select(s => new { s.TechniqueCount, s.PrimaryTeacherId, s.SecondTeacherId, s.ThirdTeacherId })
            .FirstOrDefaultAsync(ct);

        var teacherCount = student?.TechniqueCount is > 0 ? student.TechniqueCount!.Value : 1;
        var salaryEach = LateFineCalculator.TeacherSalaryEach(
            request.InstallmentAmount, request.TeacherPercentage, teacherCount);

        fee.StudentRegistrationId = reg;
        fee.FeeDate = request.FeeDate;
        fee.PaymentDate = request.PreviousPaymentDate;
        fee.Installment = request.TotalForMonth;
        fee.AgreedMonthlyFee = request.InstallmentAmount;
        fee.OtherInstallment = request.AmountPaid;
        fee.Percentage = request.TeacherPercentage;
        fee.OldDue = request.OldDue;
        fee.NewDue = request.NewDue;
        fee.ModifiedBy = actor;
        fee.ModifiedOn = DateTime.UtcNow;

        // The sibling rows are keyed by bill number, exactly as usp_UpdateData did.
        if (billNo is not null)
        {
            var fines = await _db.LateFees.Where(l => l.BillNo == billNo).ToListAsync(ct);
            foreach (var fine in fines)
            {
                fine.StudentRegistrationId = reg;
                fine.FineAmount = request.LateFine;
                fine.FineDate = request.FeeDate;
            }

            var payouts = await _db.TeacherFees.Where(t => t.BillNo == billNo).ToListAsync(ct);
            foreach (var payout in payouts)
            {
                payout.Salary = salaryEach;
                payout.PaidOn = request.FeeDate;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Fee {BillNo} (id {Id}) updated by {Actor}", billNo, id, actor);
        return Result<bool>.Success(true);
    }

    /* --------------------------------------------------------------- helpers */

    private Task<DateOnly?> GetLastPaymentDateAsync(string registrationId, CancellationToken ct) =>
        _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.StudentRegistrationId == registrationId)
            .OrderByDescending(f => f.FeeDate)
            .ThenByDescending(f => f.Id)
            .Select(f => f.FeeDate)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Latest <c>NewDue</c> carried forward. A negative value means the student is in
    /// credit, which is not a debt, so it is floored at zero.
    /// </summary>
    private async Task<decimal> GetOutstandingDueAsync(string registrationId, CancellationToken ct)
    {
        var due = await _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.StudentRegistrationId == registrationId)
            .OrderByDescending(f => f.FeeDate)
            .ThenByDescending(f => f.Id)
            .Select(f => f.NewDue)
            .FirstOrDefaultAsync(ct);

        return due is > 0m ? due.Value : 0m;
    }

    /// <summary>
    /// <c>S-M-{n}-{yy}</c>, matching the legacy format. Derived from the highest
    /// existing ledger id, then stepped past anything already taken — the legacy
    /// version did no such check and could reuse a number.
    /// </summary>
    private async Task<string> NextBillNumberAsync(CancellationToken ct)
    {
        var lastId = await _db.MonthlyFees.MaxAsync(f => (int?)f.Id, ct) ?? 0;
        var year = DateTime.Today.ToString("yy");

        var next = lastId + 1;
        var guard = 0;

        while (await _db.MonthlyFees.AnyAsync(f => f.BillNo == $"S-M-{next}-{year}", ct))
        {
            next++;
            if (++guard > 1000)
                throw new InvalidOperationException("Could not find a free bill number after 1000 attempts.");
        }

        return $"S-M-{next}-{year}";
    }
}
