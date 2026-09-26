using InstituteMS.Application.Common;
using InstituteMS.Application.ExamFees;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Exam fees: Admin's fee per course and branch, an optional per-student
/// override, and the fees collected. Charged once per course; the institute
/// keeps all of it. A student owes it once scheduled for (or having sat) an
/// exam on their course - not from the day they are admitted.
/// </summary>
public class ExamFeeService : IExamFeeService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ExamFeeService> _logger;

    public ExamFeeService(AppDbContext db, ILogger<ExamFeeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /* ------------------------------------------------------------ master */

    public async Task<IReadOnlyList<ExamFeeRateDto>> GetRatesAsync(CancellationToken ct = default)
    {
        var courses = await _db.Courses.AsNoTracking()
            .Where(c => c.Name != null && c.Name != "")
            .OrderBy(c => c.Name)
            .Select(c => new { c.Cid, c.Name })
            .ToListAsync(ct);

        var branches = await _db.Branches.AsNoTracking()
            .Where(b => b.IsActive && b.Name != null && b.Name != "")
            .OrderBy(b => b.Name)
            .Select(b => b.Name!)
            .ToListAsync(ct);

        var rates = await _db.ExamFeeRates.AsNoTracking().ToListAsync(ct);

        return courses
            .SelectMany(c => branches.Select(b => new ExamFeeRateDto(
                c.Cid, c.Name, b,
                rates.FirstOrDefault(r => r.CourseId == c.Cid
                    && string.Equals(r.Branch, b, StringComparison.OrdinalIgnoreCase))?.Amount)))
            .ToList();
    }

    public async Task<Result<IReadOnlyList<ExamFeeRateDto>>> SaveRatesAsync(
        ExamFeeRatesSaveRequest request, string? actor, CancellationToken ct = default)
    {
        if (request.Rates.Any(r => r.Amount is < 0m))
            return Result<IReadOnlyList<ExamFeeRateDto>>.Failure("An exam fee cannot be negative.");

        var existing = await _db.ExamFeeRates.ToListAsync(ct);
        var now = DateTime.Now;

        foreach (var r in request.Rates)
        {
            var branch = r.Branch.Trim();
            var row = existing.FirstOrDefault(x => x.CourseId == r.CourseId
                && string.Equals(x.Branch, branch, StringComparison.OrdinalIgnoreCase));

            if (r.Amount is null)
            {
                // Cleared: the course no longer has an exam fee at this branch.
                if (row is not null) _db.ExamFeeRates.Remove(row);
                continue;
            }

            if (row is null)
            {
                _db.ExamFeeRates.Add(new ExamFeeRate
                {
                    CourseId = r.CourseId, Branch = branch, Amount = r.Amount.Value,
                    ModifiedBy = actor, ModifiedOn = now
                });
            }
            else if (row.Amount != r.Amount.Value)
            {
                row.Amount = r.Amount.Value;
                row.ModifiedBy = actor;
                row.ModifiedOn = now;
            }
        }

        await _db.SaveChangesAsync(ct);
        return Result<IReadOnlyList<ExamFeeRateDto>>.Success(await GetRatesAsync(ct));
    }

    /* ----------------------------------------------------------- student */

    public async Task<Result<StudentExamFeeDto>> GetStudentAsync(int studentId, CancellationToken ct = default)
    {
        var status = await StatusAsync(studentId, ct);
        return status is null
            ? Result<StudentExamFeeDto>.Failure("Student not found.", ResultStatus.NotFound)
            : Result<StudentExamFeeDto>.Success(status);
    }

    public async Task<Result<StudentExamFeeDto>> GetMineAsync(string email, CancellationToken ct = default)
    {
        /* The student's own record, matched the way ReportService does it (any of
           their addresses, active or completed) - done here rather than through
           IReportService, which itself uses this service for the statement. */
        var trimmed = email?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return Result<StudentExamFeeDto>.Failure("No signed-in identity to resolve.", ResultStatus.Unauthorized);

        var id = await _db.Students.AsNoTracking()
            .Where(s => (s.IsActive == true || s.IsComplete == true)
                        && (s.Email == trimmed || s.PersonalEmail == trimmed || s.AvijanLoginEmail == trimmed))
            .OrderByDescending(s => s.IsActive == true)
            .ThenByDescending(s => s.Id)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);

        return id is int studentId
            ? await GetStudentAsync(studentId, ct)
            : Result<StudentExamFeeDto>.Failure("Student not found.", ResultStatus.NotFound);
    }

    public async Task<Result<StudentExamFeeDto>> CollectAsync(
        int studentId, ExamFeeCollectRequest request, string? actor, CancellationToken ct = default)
    {
        if (request.PaymentMode is not ("Cash" or "Bank"))
            return Result<StudentExamFeeDto>.Failure("Payment mode must be Cash or Bank.");

        if (request.Amount <= 0m)
            return Result<StudentExamFeeDto>.Failure("Enter the amount received.");

        var status = await StatusAsync(studentId, ct);
        if (status is null)
            return Result<StudentExamFeeDto>.Failure("Student not found.", ResultStatus.NotFound);

        if (status.ExamFee is null)
            return Result<StudentExamFeeDto>.Failure(
                $"No exam fee is set for {status.CourseName} at {status.Branch ?? "this branch"}. " +
                "Ask Admin to set it on the Exam Fees screen.");

        if (status.Due <= 0m)
            return Result<StudentExamFeeDto>.Failure("This student's exam fee is already paid.");

        // Refused rather than capped, as with admission dues: an over-payment is
        // a mistyped figure or the wrong student.
        if (request.Amount > status.Due)
            return Result<StudentExamFeeDto>.Failure(
                $"That is more than the exam fee still owed ({status.Due:0.##}).");

        _db.ExamFeePayments.Add(new ExamFeePayment
        {
            StudentId = studentId,
            RegistrationId = status.RegistrationId,
            CourseId = status.CourseId,
            Branch = status.Branch,
            Amount = request.Amount,
            PaymentMode = request.PaymentMode,
            PaidOn = request.PaidOn ?? DateOnly.FromDateTime(DateTime.Today),
            Note = request.Note?.Trim(),
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        });

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Exam fee {Amount} collected for student {StudentId} by {Actor}",
            request.Amount, studentId, actor);

        return Result<StudentExamFeeDto>.Success((await StatusAsync(studentId, ct))!);
    }

    public async Task<Result<bool>> DeletePaymentAsync(int paymentId, CancellationToken ct = default)
    {
        var payment = await _db.ExamFeePayments.FirstOrDefaultAsync(p => p.Id == paymentId, ct);
        if (payment is null)
            return Result<bool>.Failure("That exam fee payment no longer exists.", ResultStatus.NotFound);

        _db.ExamFeePayments.Remove(payment);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<StudentExamFeeDto>> SetOverrideAsync(
        int studentId, ExamFeeOverrideRequest request, string? actor, CancellationToken ct = default)
    {
        if (request.Amount is < 0m)
            return Result<StudentExamFeeDto>.Failure("An exam fee cannot be negative.");

        var student = await _db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null)
            return Result<StudentExamFeeDto>.Failure("Student not found.", ResultStatus.NotFound);

        var paid = await _db.ExamFeePayments
            .Where(p => p.StudentId == studentId && p.CourseId == student.CourseId)
            .SumAsync(p => (decimal?)p.Amount ?? 0m, ct);

        if (request.Amount is { } amount && amount < paid)
            return Result<StudentExamFeeDto>.Failure(
                $"The student has already paid {paid:0.##}; the exam fee cannot be set below that.");

        student.ExamFeeOverride = request.Amount;
        student.ModifiedBy = actor;
        student.ModifiedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);

        return Result<StudentExamFeeDto>.Success((await StatusAsync(studentId, ct))!);
    }

    /* ---------------------------------------------------------- reports */

    public async Task<IReadOnlyList<ExamFeeDueRowDto>> GetDuesAsync(string? branch, CancellationToken ct = default, int? courseId = null)
    {
        var scheduled = await ScheduledStudentIdsAsync(null, ct);
        if (scheduled.Count == 0) return [];

        var students = await _db.Students.AsNoTracking()
            .Where(s => scheduled.Contains(s.Id) && (branch == null || s.Branch == branch)
                        && (courseId == null || s.CourseId == courseId))
            .Select(s => new
            {
                s.Id, s.RegistrationId, s.Name, s.Phone, s.Branch, s.CourseId, s.ExamFeeOverride,
                CourseName = s.Course != null ? s.Course.Name : null
            })
            .ToListAsync(ct);

        var rates = await _db.ExamFeeRates.AsNoTracking().ToListAsync(ct);
        var ids = students.Select(s => s.Id).ToList();
        var payments = await _db.ExamFeePayments.AsNoTracking()
            .Where(p => ids.Contains(p.StudentId))
            .Select(p => new { p.StudentId, p.CourseId, p.Amount })
            .ToListAsync(ct);

        return students
            .Select(s =>
            {
                var fee = s.ExamFeeOverride ?? RateFor(rates, s.CourseId, s.Branch);
                var paid = payments.Where(p => p.StudentId == s.Id && p.CourseId == s.CourseId).Sum(p => p.Amount);
                return (s, fee, paid);
            })
            .Where(x => x.fee is decimal f && f - x.paid > 0m)
            .Select(x => new ExamFeeDueRowDto(
                x.s.Id, x.s.RegistrationId, x.s.Name, x.s.Phone, x.s.Branch, x.s.CourseName,
                x.fee!.Value, x.paid, x.fee.Value - x.paid))
            .OrderBy(r => r.Branch).ThenBy(r => r.StudentName)
            .ToList();
    }

    public async Task<HashSet<string>> UnpaidAmongAsync(IEnumerable<string> registrationIds, CancellationToken ct = default)
    {
        var regs = registrationIds.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().ToList();
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (regs.Count == 0) return result;

        var students = await _db.Students.AsNoTracking()
            .Where(s => s.RegistrationId != null && regs.Contains(s.RegistrationId))
            .Select(s => new { s.Id, s.RegistrationId, s.CourseId, s.Branch, s.ExamFeeOverride })
            .ToListAsync(ct);

        var rates = await _db.ExamFeeRates.AsNoTracking().ToListAsync(ct);
        var ids = students.Select(s => s.Id).ToList();
        var payments = await _db.ExamFeePayments.AsNoTracking()
            .Where(p => ids.Contains(p.StudentId))
            .Select(p => new { p.StudentId, p.CourseId, p.Amount })
            .ToListAsync(ct);

        foreach (var s in students)
        {
            var fee = s.ExamFeeOverride ?? RateFor(rates, s.CourseId, s.Branch);
            if (fee is not decimal f) continue;   // no fee set: nothing to warn about
            var paid = payments.Where(p => p.StudentId == s.Id && p.CourseId == s.CourseId).Sum(p => p.Amount);
            if (f - paid > 0m) result.Add(s.RegistrationId!);
        }

        return result;
    }

    /* ----------------------------------------------------------- helpers */

    private async Task<StudentExamFeeDto?> StatusAsync(int studentId, CancellationToken ct)
    {
        var s = await _db.Students.AsNoTracking()
            .Where(x => x.Id == studentId)
            .Select(x => new
            {
                x.Id, x.RegistrationId, x.Name, x.CourseId, x.Branch, x.ExamFeeOverride,
                CourseName = x.Course != null ? x.Course.Name : null
            })
            .FirstOrDefaultAsync(ct);
        if (s is null) return null;

        var standard = RateFor(await _db.ExamFeeRates.AsNoTracking().ToListAsync(ct), s.CourseId, s.Branch);
        var fee = s.ExamFeeOverride ?? standard;

        var payments = await _db.ExamFeePayments.AsNoTracking()
            .Where(p => p.StudentId == s.Id && p.CourseId == s.CourseId)
            .OrderByDescending(p => p.PaidOn).ThenByDescending(p => p.Id)
            .Select(p => new ExamFeePaymentDto(p.Id, p.PaidOn, p.Amount, p.PaymentMode, p.Note, p.CreatedBy))
            .ToListAsync(ct);

        var paid = payments.Sum(p => p.Amount);
        var scheduled = (await ScheduledStudentIdsAsync(s.Id, ct)).Contains(s.Id);

        return new StudentExamFeeDto(
            s.Id, s.RegistrationId, s.Name, s.CourseId, s.CourseName, s.Branch,
            standard, s.ExamFeeOverride, fee, paid,
            fee is decimal f ? Math.Max(0m, f - paid) : 0m,
            scheduled, payments);
    }

    private static decimal? RateFor(List<ExamFeeRate> rates, int? courseId, string? branch) =>
        courseId is null || string.IsNullOrWhiteSpace(branch)
            ? null
            : rates.FirstOrDefault(r => r.CourseId == courseId
                && string.Equals(r.Branch, branch, StringComparison.OrdinalIgnoreCase))?.Amount;

    /// <summary>Students scheduled for, or who have sat, an exam on their current course.</summary>
    private async Task<HashSet<int>> ScheduledStudentIdsAsync(int? onlyStudentId, CancellationToken ct)
    {
        var students = _db.Students.AsNoTracking()
            .Where(s => onlyStudentId == null || s.Id == onlyStudentId);

        var viaSchedule = await students
            .Where(s => _db.ExamSchedules.Any(e => e.StudentRegistrationId == s.RegistrationId
                && _db.ExamPapers.Any(p => p.ExamId == e.ExamId && p.CourseId == s.CourseId)))
            .Select(s => s.Id)
            .ToListAsync(ct);

        var viaAttempt = await students
            .Where(s => _db.ExamAttempts.Any(a => a.StudentRegistrationId == s.RegistrationId
                && a.CourseId == s.CourseId))
            .Select(s => s.Id)
            .ToListAsync(ct);

        return viaSchedule.Concat(viaAttempt).ToHashSet();
    }
}
