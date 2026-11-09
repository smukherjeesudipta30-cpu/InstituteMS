using InstituteMS.Application.Documents;
using InstituteMS.Application.Notifications;
using InstituteMS.Application.Common;
using InstituteMS.Application.Fees;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InstituteMS.Infrastructure.Settings;

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
    private readonly PaymentSettings _payments;
    private readonly IStudentMailer _mailer;
    private readonly IReceiptPdfGenerator _pdf;
    private readonly IReceiptLinkService _links;

    public FeeService(
        AppDbContext db,
        ILogger<FeeService> logger,
        IOptions<PaymentSettings> payments,
        IStudentMailer mailer,
        IReceiptPdfGenerator pdf,
        IReceiptLinkService links)
    {
        _db = db;
        _logger = logger;
        _payments = payments.Value;
        _mailer = mailer;
        _pdf = pdf;
        _links = links;
    }

    /// <summary>
    /// Emails the student their money receipt after a payment has been committed.
    /// </summary>
    /// <remarks>
    /// Always called <b>after</b> the transaction, never inside it: a slow or
    /// unreachable mail server must not hold a database transaction open. It is
    /// awaited rather than fired and forgotten so the attempt is written to
    /// <c>dbo.MailLog_registration_time</c> before the response returns, and the
    /// mailer never throws — a fee that has been taken stays taken whether or not
    /// the message went out.
    /// </remarks>
    private async Task SendReceiptAsync(int feeId, string? actor, CancellationToken ct)
    {
        var receipt = await GetReceiptAsync(feeId, ct);
        if (!receipt.Succeeded || receipt.Value is null) return;

        var r = receipt.Value;

        // The receipt itself does not carry the personal address, so it is read
        // here rather than widening the DTO the printed receipt shares.
        var personalEmail = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == r.RegistrationId)
            .OrderByDescending(s => s.Id)
            .Select(s => s.PersonalEmail)
            .FirstOrDefaultAsync(ct);

        /* The PDF is an addition to the message, never a condition of it: a receipt
           that fails to render must still reach the student as the body it always
           was. Hence the try — the mailer treats a null attachment as "no PDF". */
        byte[]? pdf = null;
        try { pdf = _pdf.MoneyReceipt(r); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not render the PDF for receipt {BillNo}; sending without it.", r.BillNo);
        }

        await _mailer.SendFeeReceiptAsync(
            new FeeReceiptMailRequest(
                r.RegistrationId ?? string.Empty,
                r.StudentName ?? string.Empty,
                r.CourseName,
                personalEmail,
                r.BillNo,
                r.AmountPaid,
                r.AmountPaidInWords,
                r.NewDue,
                r.FeeDate,
                r.NextDueDate,
                pdf,
                $"Avijan-Money-Receipt-{(r.BillNo ?? "receipt").Replace('/', '-')}.pdf"),
            actor, ct);
    }

    /* ------------------------------------------------------------------ read */

    public async Task<Result<StudentFeeBillDto>> GetBillAsync(
        string registrationId, CancellationToken ct = default)
    {
        var reg = registrationId.Trim();

        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == reg && s.IsActive == true)
            .OrderByDescending(s => s.Id)
            .Select(s => new
            {
                s.Id, s.RegistrationId, s.Name, s.Phone, s.PersonalEmail,
                s.AdmissionFees, s.AdmissionFeesPaid, s.MonthlyFees,
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
            installmentsPaid, lastPayment, outstandingDue,
            student.Phone, student.PersonalEmail,
            /* Null AdmissionFeesPaid predates migration 051, where it meant paid in
               full — so it leaves no due rather than the whole fee owing. */
            Math.Max(0m, (student.AdmissionFees ?? 0m) - (student.AdmissionFeesPaid ?? student.AdmissionFees ?? 0m))));
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
                f.Installment, f.OtherInstallment, f.Percentage, f.OldDue, f.NewDue,
                f.PaymentMode
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
                fines.FirstOrDefault(f => f.BillNo == r.BillNo)?.FineAmount,
                r.PaymentMode))
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

        // Reported for the desk, not used for the fine — see the DTO.
        var sinceLastPayment = student.LastPaymentDate is { } last
            ? Math.Abs(request.FeeDate.DayNumber - last.DayNumber)
            : (int?)null;

        return Result<FeeCalculationDto>.Success(new FeeCalculationDto(
            fine.DaysSincePrevious,
            fine.DaysPerInstallment,
            fine.LateFine,
            total,
            student.OutstandingDue,
            decimal.Round(newDue, 2),
            fine.ReferenceDate,
            sinceLastPayment,
            student.LastPaymentDate,
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
            payment.OldDue, payment.NewDue, payment.FeeDate, nextDue,
            _links.CreateUrl(ReceiptKind.Fee, payment.Id)));
    }

    /* ----------------------------------------------------------------- write */

    public async Task<Result<FeeRecordedDto>> RecordPaymentAsync(
        FeeSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var reg = request.RegistrationId.Trim();

        var student = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == reg && s.IsActive == true)
            .OrderByDescending(s => s.Id)
            .Select(s => new
            {
                s.TechniqueCount, s.MonthlyFees, s.Branch,
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

        // Teachers paid another way (Teacher.NoFeeShare) still get their row, at 0.
        var noShare = await NoFeeShareTeachersAsync(teacherIds, ct);

        var strategy = _db.Database.CreateExecutionStrategy();

        var recorded = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            /* 1. the fee ledger row.

               The bill number is filled in AFTER this insert, from the row's own
               identity — see below. It cannot be worked out first, which is what
               the previous version did with MAX(id) + 1. */
            var fee = new MonthlyFee
            {
                StudentRegistrationId = reg,
                FeeDate = request.FeeDate,
                PaymentDate = request.PreviousPaymentDate,
                Installment = request.TotalForMonth,   // legacy stores installment + fine here
                // Pin the agreed fee onto the row, so a later fee change cannot make
                // this payment unreadable (db/migrations/010).
                AgreedMonthlyFee = request.InstallmentAmount,
                /* The branch is taken from the student, never from the request: a
                   fee belongs to the branch the student is enrolled at, and there is
                   deliberately no way for a screen to nominate a different one.
                   Stamped here so a later transfer cannot move this payment
                   (db/migrations/030). */
                Branch = student.Branch,
                OtherInstallment = request.AmountPaid,
                Percentage = request.TeacherPercentage,
                OldDue = request.OldDue,
                NewDue = request.NewDue,
                PaymentMode = request.PaymentMode,
                CreatedBy = actor,
                CreatedOn = DateTime.Now
            };
            _db.MonthlyFees.Add(fee);

            /* Saved on its own so SQL Server assigns the identity, and the bill
               number is then taken FROM that identity rather than predicted before
               it. Everything below is keyed on the bill number, so it has to exist
               before those rows are built.
               -----------------------------------------------------------------
               The old code read MAX(id) + 1 and used that. The two agree only while
               nothing has ever disturbed the identity counter - but a deleted row,
               a rolled-back insert, or a SQL Server restart (which can advance an
               identity by up to 1000) leaves MAX(id) behind the next identity
               value. The row then gets one id and carries a bill number one lower,
               and the NEXT bill number is computed from the new MAX - so a number
               is skipped. That is the "after 66 comes 68" gap: id 17767 was issued
               bill S-M-17766, and the following bill became S-M-17768.

               Deriving it from the identity makes bill number and row id the same
               value by construction, so they can never drift again. */
            await _db.SaveChangesAsync(ct);

            var billNo = $"S-M-{fee.Id}-{DateTime.Today:yy}";
            fee.BillNo = billNo;

            // 2. the income roll-up the reports read
            _db.FeeTotals.Add(new FeeTotal
            {
                StudentRegistrationId = reg,
                Total = request.TotalForMonth,
                StoreDate = request.FeeDate,
                CreatedBy = actor,
                CreatedOn = DateTime.Now
            });

            // 3. the fine, always written so the late-fine report stays complete
            _db.LateFees.Add(new LateFee
            {
                StudentRegistrationId = reg,
                BillNo = billNo,
                FineAmount = request.LateFine,
                FineDate = request.FeeDate,
                CreatedBy = actor,
                CreatedOn = DateTime.Now
            });

            // 4. one payout row per assigned teacher - 0 for one who takes no fee
            // share, so the bill still counts them and a co-teacher's share holds.
            foreach (var teacherId in teacherIds)
            {
                _db.TeacherFees.Add(new TeacherFee
                {
                    TeacherId = teacherId,
                    StudentRegistrationId = reg,
                    BillNo = billNo,
                    Salary = noShare.Contains(teacherId) ? 0m : salaryEach,
                    PaidOn = request.FeeDate,
                    CreatedBy = actor,
                    CreatedOn = DateTime.Now
                });
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new FeeRecordedDto(fee.Id, billNo, salaryEach, teacherIds.Count(t => !noShare.Contains(t)));
        });

        _logger.LogInformation(
            "Fee {BillNo} recorded for {RegistrationId}: total {Total}, paid {Paid}, fine {Fine}, {Teachers} teacher payout(s) of {Each} by {Actor}",
            recorded.BillNo, reg, request.TotalForMonth, request.AmountPaid, request.LateFine,
            teacherIds.Count, salaryEach, actor);

        await SendReceiptAsync(recorded.Id, actor, ct);

        return Result<FeeRecordedDto>.Success(recorded);
    }

    /// <summary>Of these teachers, the ones who take no share of student fees (Teacher.NoFeeShare).</summary>
    private async Task<HashSet<int>> NoFeeShareTeachersAsync(IEnumerable<int> teacherIds, CancellationToken ct)
    {
        var ids = teacherIds.Distinct().ToList();
        if (ids.Count == 0) return [];

        return (await _db.Teachers.AsNoTracking()
            .Where(t => ids.Contains(t.Id) && t.NoFeeShare)
            .Select(t => t.Id)
            .ToListAsync(ct)).ToHashSet();
    }

    public async Task<Result<bool>> UpdatePaymentAsync(
        int id, FeeSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var fee = await _db.MonthlyFees.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fee is null)
            return Result<bool>.Failure("Payment not found.", ResultStatus.NotFound);

        var reg = request.RegistrationId.Trim();
        var billNo = fee.BillNo;

        // How many teachers this SPECIFIC bill was already split across — the
        // number of TeacherFee rows already on it, not the student's current
        // TechniqueCount, which can have changed since the bill was first
        // recorded (a reassignment, or a correction to the technique count
        // itself). Editing a bill corrects its amount for the teachers already
        // on it; it does not re-attribute the bill to a different set of
        // teachers or a different count. Falls back to the student's own
        // TechniqueCount only when the bill has no payout rows yet to count.
        var existingPayoutCount = billNo is null
            ? 0
            : await _db.TeacherFees.CountAsync(t => t.BillNo == billNo, ct);

        int teacherCount;
        if (existingPayoutCount > 0)
        {
            teacherCount = existingPayoutCount;
        }
        else
        {
            var student = await _db.Students
                .AsNoTracking()
                .Where(s => s.RegistrationId == reg)
                .OrderByDescending(s => s.Id)
                .Select(s => new { s.TechniqueCount })
                .FirstOrDefaultAsync(ct);

            teacherCount = student?.TechniqueCount is > 0 ? student.TechniqueCount!.Value : 1;
        }

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
        fee.PaymentMode = request.PaymentMode;
        fee.ModifiedBy = actor;
        fee.ModifiedOn = DateTime.Now;

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
            var noShare = await NoFeeShareTeachersAsync(
                payouts.Where(p => p.TeacherId is not null).Select(p => p.TeacherId!.Value), ct);
            foreach (var payout in payouts)
            {
                payout.Salary = payout.TeacherId is int tid && noShare.Contains(tid) ? 0m : salaryEach;
                payout.PaidOn = request.FeeDate;
            }
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Fee {BillNo} (id {Id}) updated by {Actor}", billNo, id, actor);
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// The reverse of <see cref="RecordPaymentAsync"/>: removes the ledger row and
    /// every sibling row <see cref="RecordPaymentAsync"/> wrote alongside it, all
    /// keyed to the same bill number, in one transaction — for a payment entered
    /// by mistake or duplicated, which until now had no way back out.
    /// </summary>
    public async Task<Result<bool>> DeletePaymentAsync(int id, CancellationToken ct = default)
    {
        var fee = await _db.MonthlyFees.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fee is null)
            return Result<bool>.Failure("Payment not found.", ResultStatus.NotFound);

        var billNo = fee.BillNo;
        var reg = fee.StudentRegistrationId;

        var strategy = _db.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            if (billNo is not null)
            {
                _db.TeacherFees.RemoveRange(_db.TeacherFees.Where(t => t.BillNo == billNo));
                _db.LateFees.RemoveRange(_db.LateFees.Where(l => l.BillNo == billNo));
            }

            // FeeTotal carries no bill number — it is matched the same way the
            // report that reads it does, by student and the exact date/amount this
            // payment wrote.
            _db.FeeTotals.RemoveRange(_db.FeeTotals.Where(t =>
                t.StudentRegistrationId == reg && t.StoreDate == fee.FeeDate && t.Total == fee.Installment));

            _db.MonthlyFees.Remove(fee);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        _logger.LogInformation("Fee {BillNo} (id {Id}) deleted", billNo, id);
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


    /* =========================== online payment from the student portal */

    /// <summary>
    /// The student behind a sign-in. Duplicated from <c>ReportService</c> rather
    /// than injected: <c>ReportService</c> already depends on this service, so
    /// taking a dependency the other way would close a cycle.
    /// </summary>
    private async Task<Result<string>> ResolveStudentAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email))
            return Result<string>.Failure("No signed-in identity to resolve.", ResultStatus.Unauthorized);

        var trimmed = email.Trim();

        // Deliberately not filtered on active or complete, unlike the report
        // version: an inactive student still has a record, and telling them their
        // sign-in matches nothing would be untrue. The caller checks their status
        // and says plainly why they cannot pay. Active first, then newest, so a
        // stale duplicate never wins over a live registration.
        var regId = await _db.Students
            .AsNoTracking()
            .Where(s => s.Email == trimmed
                        || s.PersonalEmail == trimmed
                        || s.AvijanLoginEmail == trimmed)
            .OrderByDescending(s => s.IsActive == true)
            .ThenByDescending(s => s.Id)
            .Select(s => s.RegistrationId)
            .FirstOrDefaultAsync(ct);

        return string.IsNullOrEmpty(regId)
            ? Result<string>.Failure(
                "No student record is linked to this sign-in. Ask the office to check the email on your registration.",
                ResultStatus.NotFound)
            : Result<string>.Success(regId);
    }

    public async Task<Result<MyFeeDueDto>> GetMyDueAsync(
        string email, CancellationToken ct = default)
    {
        var own = await ResolveStudentAsync(email, ct);

        if (!own.Succeeded)
            return Result<MyFeeDueDto>.Failure(own.Error!, own.Status);

        var regId = own.Value!;

        /* ---- who may pay a monthly fee at all ----------------------------
           Only a student still studying. A completed student has no further
           months to pay, and an inactive one has left — taking money from
           either would put a fee on a ledger that should have stopped.

           Checked explicitly rather than left to GetBillAsync's active-only
           filter, which blocked them by accident with a back-office message
           ("No active student found with registration id ...") and let a
           student marked complete while still flagged active straight
           through. */

        var status = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId == regId)
            .OrderByDescending(s => s.IsActive == true)
            .ThenByDescending(s => s.Id)
            .Select(s => new { IsActive = s.IsActive == true, IsComplete = s.IsComplete == true, s.Branch })
            .FirstOrDefaultAsync(ct);

        if (status is null)
        {
            return Result<MyFeeDueDto>.Failure(
                "No student record is linked to this sign-in. Ask the office to check " +
                "the email on your registration.",
                ResultStatus.NotFound);
        }

        if (status.IsComplete)
        {
            return Result<MyFeeDueDto>.Failure(
                "You have completed your course, so there are no further monthly fees " +
                "to pay. If you think this is wrong, please speak to the office.",
                ResultStatus.Forbidden);
        }

        if (!status.IsActive)
        {
            return Result<MyFeeDueDto>.Failure(
                "Your registration is not active, so fees cannot be paid online. " +
                "Please speak to the office.",
                ResultStatus.Forbidden);
        }

        var bill = await GetBillAsync(regId, ct);

        if (!bill.Succeeded)
            return Result<MyFeeDueDto>.Failure(bill.Error!, bill.Status);

        var b = bill.Value!;

        var (upiId, payeeName) = await UpiForBranchAsync(status.Branch, ct);

        /* The payment is being made today, so today is the fee date — exactly as the
           counter records it.

           This used to be the *due* date (last payment + 32 days), which quietly made
           the portal cheaper than the desk: the fine is worked out from the fee date,
           so pricing the month as though it were paid on the day it fell due meant the
           fine stopped growing however late the student actually was. A student whose
           fee was due on 23 Jul was charged no fine at all in the portal on 1 Sep,
           while the same student at the counter that morning was charged 24 rupees.

           It also mis-dated the ledger: the row was written with a fee date weeks in
           the past, so a September payment landed in August and the dues and income
           reports, which filter on that date, both disagreed with the cash. */
        var today = DateOnly.FromDateTime(DateTime.Now);

        var dueDate = b.LastPaymentDate is { } last
            ? LateFineCalculator.NextDueDate(last)
            : (DateOnly?)null;

        var feeDate = today;

        var monthlyFee = b.MonthlyFees ?? 0m;

        var calc = await CalculateAsync(
            new FeeCalculationRequest(regId, feeDate, monthlyFee, null), ct);

        var fine = calc.Succeeded ? calc.Value!.LateFine : 0m;
        var explanation = calc.Succeeded ? calc.Value!.Explanation : null;
        var oldDue = calc.Succeeded ? calc.Value!.OldDue : 0m;

        string? reason = null;

        if (monthlyFee <= 0m)
        {
            reason = "No monthly fee is set on your record. Please speak to the office.";
        }
        else if (string.IsNullOrWhiteSpace(upiId))
        {
            reason = "Online payment is not switched on yet. Please pay at the centre.";
        }
        else if (b.LastPaymentDate is null)
        {
            // The teacher's share is carried over from the previous payment, and a
            // first payment has none to carry. Taking it at the counter is what sets
            // the percentage; after that the portal can repeat it.
            reason = "Your first payment is taken at the centre. Once it is recorded " +
                     "you can pay the following months from here.";
        }

        return Result<MyFeeDueDto>.Success(new MyFeeDueDto(
            regId,
            b.StudentName,
            b.CourseName,
            feeDate,
            dueDate,
            monthlyFee,
            fine,
            monthlyFee + fine,
            oldDue,
            explanation,
            b.LastPaymentDate,
            reason is null,
            reason,
            upiId,
            payeeName));
    }

    /// <summary>
    /// The UPI account a student of this branch pays into: one set on the branch
    /// in the Branches screen, else one configured for the branch by name
    /// (Payments:Branches — Champahati), else the institute's usual account.
    /// Rajpur has neither, so its students pay exactly as before.
    /// </summary>
    private async Task<(string? UpiId, string? PayeeName)> UpiForBranchAsync(string? branch, CancellationToken ct)
    {
        var name = branch?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            var own = await _db.Branches.AsNoTracking()
                .Where(b => b.Name == name && b.UpiId != null && b.UpiId != "")
                .Select(b => new { b.UpiId, b.UpiPayeeName })
                .FirstOrDefaultAsync(ct);
            if (own is not null)
                return (own.UpiId, string.IsNullOrWhiteSpace(own.UpiPayeeName) ? _payments.PayeeName : own.UpiPayeeName);

            if (_payments.Branches.TryGetValue(name, out var configured) && !string.IsNullOrWhiteSpace(configured.UpiId))
                return (configured.UpiId, string.IsNullOrWhiteSpace(configured.PayeeName) ? _payments.PayeeName : configured.PayeeName);
        }

        return (_payments.UpiId, _payments.PayeeName);
    }

    public async Task<Result<FeeRecordedDto>> PayOnlineAsync(
        string email, OnlineFeePaymentRequest request, CancellationToken ct = default)
    {
        var due = await GetMyDueAsync(email, ct);

        if (!due.Succeeded)
            return Result<FeeRecordedDto>.Failure(due.Error!, due.Status);

        var d = due.Value!;

        if (!d.CanPay)
            return Result<FeeRecordedDto>.Failure(d.Reason ?? "This fee cannot be paid online.");

        var reference = request.TransactionId.Trim();

        // The same reference twice is a double submission — a student tapping the
        // button again, or a page refresh — not a second month's fee. The unique
        // index enforces it; catching it here explains what happened.
        var alreadyUsed = await _db.MonthlyFees
            .AsNoTracking()
            .AnyAsync(f => f.TransactionId == reference, ct);

        if (alreadyUsed)
        {
            return Result<FeeRecordedDto>.Failure(
                "That reference has already been submitted. Check your fee history — " +
                "the payment is most likely already recorded.",
                ResultStatus.Conflict);
        }

        // "Like last month": the agreed teacher share is recorded nowhere but on the
        // previous payment, so it is carried forward rather than guessed.
        var lastPercentage = await _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.StudentRegistrationId == d.RegistrationId && f.Percentage != null)
            .OrderByDescending(f => f.PaymentDate)
            .ThenByDescending(f => f.Id)
            .Select(f => f.Percentage)
            .FirstOrDefaultAsync(ct);

        if (lastPercentage is null)
        {
            return Result<FeeRecordedDto>.Failure(
                "Your first payment is taken at the centre. Once it is recorded you " +
                "can pay the following months from here.");
        }

        var save = new FeeSaveRequest
        {
            RegistrationId = d.RegistrationId,
            FeeDate = d.FeeDate,
            PreviousPaymentDate = d.PreviousPaymentDate,
            InstallmentAmount = d.MonthlyFee,
            TeacherPercentage = lastPercentage.Value,
            LateFine = d.LateFine,
            TotalForMonth = d.AmountPayable,
            AmountPaid = d.AmountPayable,
            OldDue = d.OldDue,
            // Paying the month in full leaves whatever was outstanding before.
            NewDue = d.OldDue,
            // A student paying their own fee from the portal is, by construction,
            // paying by UPI against the QR code — there is no counter involved.
            PaymentMode = "Bank"
        };

        var recorded = await RecordPaymentAsync(save, PortalActor(d.RegistrationId), ct);

        if (!recorded.Succeeded)
            return recorded;

        // Stamp the reference onto the row just written, so it is identifiable as
        // an online payment and findable while it is still unverified.
        var fee = await _db.MonthlyFees
            .FirstOrDefaultAsync(f => f.BillNo == recorded.Value!.BillNo, ct);

        if (fee is not null)
        {
            fee.TransactionId = reference;
            await _db.SaveChangesAsync(ct);
        }

        return recorded;
    }

    /// <summary>
    /// Who to record the payment against. The student, named, so the ledger never
    /// suggests a member of staff took money they never handled.
    /// </summary>
    private static string PortalActor(string registrationId)
        => $"portal:{registrationId}";

    public async Task<Result<IReadOnlyList<OnlinePaymentNoticeDto>>> GetUnverifiedOnlinePaymentsAsync(
        int take = 25, CancellationToken ct = default)
    {
        var rows = await _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.TransactionId != null && f.VerifiedOn == null)
            .OrderByDescending(f => f.CreatedOn)
            .Take(Math.Clamp(take, 1, 100))
            .Select(f => new
            {
                f.Id, f.BillNo, f.StudentRegistrationId, f.FeeDate,
                f.AgreedMonthlyFee, f.Installment, f.OtherInstallment,
                f.TransactionId, f.CreatedOn
            })
            .ToListAsync(ct);

        var regIds = rows.Select(r => r.StudentRegistrationId).Distinct().ToList();

        var students = await _db.Students
            .AsNoTracking()
            .Where(s => regIds.Contains(s.RegistrationId))
            .Select(s => new
            {
                s.RegistrationId,
                s.Name,
                CourseName = s.Course != null ? s.Course.Name : null
            })
            .ToListAsync(ct);

        var billNos = rows.Select(r => r.BillNo).ToList();

        var fines = await _db.LateFees
            .AsNoTracking()
            .Where(l => billNos.Contains(l.BillNo))
            .Select(l => new { l.BillNo, l.FineAmount })
            .ToListAsync(ct);

        var notices = rows.Select(r =>
        {
            var student = students.FirstOrDefault(s =>
                string.Equals(s.RegistrationId, r.StudentRegistrationId, StringComparison.OrdinalIgnoreCase));

            var fine = fines.FirstOrDefault(f =>
                string.Equals(f.BillNo, r.BillNo, StringComparison.OrdinalIgnoreCase))?.FineAmount ?? 0m;

            return new OnlinePaymentNoticeDto(
                r.Id,
                r.BillNo,
                r.StudentRegistrationId,
                student?.Name,
                student?.CourseName,
                r.FeeDate,
                r.AgreedMonthlyFee ?? 0m,
                fine,
                r.OtherInstallment ?? r.Installment ?? 0m,
                r.TransactionId,
                // Recorded when the payment was taken; NULL only for rows that
                // predate the audit columns, which no online payment does.
                r.CreatedOn ?? default);
        }).ToList();

        return Result<IReadOnlyList<OnlinePaymentNoticeDto>>.Success(notices);
    }

    public async Task<Result<bool>> VerifyOnlinePaymentAsync(
        int feeId, string? actor, CancellationToken ct = default)
    {
        var fee = await _db.MonthlyFees.FirstOrDefaultAsync(f => f.Id == feeId, ct);

        if (fee is null)
            return Result<bool>.Failure("That payment no longer exists.", ResultStatus.NotFound);

        if (fee.TransactionId is null)
            return Result<bool>.Failure("That payment was taken at the counter, so there is nothing to verify.");

        if (fee.VerifiedOn is not null)
            return Result<bool>.Success(true);

        fee.VerifiedOn = DateTime.Now;
        fee.VerifiedBy = actor;

        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
