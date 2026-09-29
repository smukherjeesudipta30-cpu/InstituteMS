using InstituteMS.Application.Common;
using InstituteMS.Application.Masters;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Teacher master. Replaces <c>TeacherController</c> + <c>Models/Teacher.cs</c>
/// (MIGRATION_PLAN §4.4, §5.4).
/// </summary>
public class TeacherService : ITeacherService
{
    /// <summary>Marker the data migration used for stand-in teacher rows.</summary>
    /* Placeholder teachers were created by migration 004 against the migrated
       database. The legacy database has no such rows and dbo.Teacher has no
       CreatedBy column to mark them with, so nothing is treated as a stand-in. */

    private readonly AppDbContext _db;
    private readonly ILogger<TeacherService> _logger;

    public TeacherService(AppDbContext db, ILogger<TeacherService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<TeacherListItemDto>> GetListAsync(
        TeacherQuery query, CancellationToken ct = default)
    {
        var teachers = _db.Teachers.AsNoTracking().AsQueryable();

        // Nothing to exclude: placeholder teachers came from migration 004, which
        // never ran against the legacy database, and dbo.Teacher there has no
        // CreatedBy column to identify them by.

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            teachers = teachers.Where(t =>
                (t.Name != null && t.Name.Contains(term)) ||
                (t.LoginId != null && t.LoginId.Contains(term)) ||
                (t.Phone != null && t.Phone.Contains(term)));
        }

        var totalCount = await teachers.CountAsync(ct);

        var page = await teachers
            .OrderBy(t => t.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.LoginId,
                t.Phone,
                t.PersonalEmail,
                t.Designation,
                t.Qualification,
                t.ComputerTraining,
                t.Branch,
                t.IsTechnical,
                IsPlaceholder = false
            })
            .ToListAsync(ct);

        var items = await DecorateAsync(page.Select(t =>
            (t.Id, t.Name, t.LoginId, t.Phone, t.PersonalEmail, t.Designation, t.Qualification, t.ComputerTraining, t.Branch, t.IsTechnical, t.IsPlaceholder)).ToList(), ct);

        return PagedResult<TeacherListItemDto>.Create(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<Result<TeacherListItemDto>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var teacher = await _db.Teachers
            .AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.LoginId,
                t.Phone,
                t.PersonalEmail,
                t.Designation,
                t.Qualification,
                t.ComputerTraining,
                t.Branch,
                t.IsTechnical,
                IsPlaceholder = false
            })
            .FirstOrDefaultAsync(ct);

        if (teacher is null)
            return Result<TeacherListItemDto>.Failure("Teacher not found.", ResultStatus.NotFound);

        var decorated = await DecorateAsync(
            [(teacher.Id, teacher.Name, teacher.LoginId, teacher.Phone, teacher.PersonalEmail, teacher.Designation, teacher.Qualification, teacher.ComputerTraining, teacher.Branch, teacher.IsTechnical, teacher.IsPlaceholder)], ct);

        return Result<TeacherListItemDto>.Success(decorated[0]);
    }

    public async Task<bool> LoginIdExistsAsync(
        string loginId, int? excludingTeacherId, CancellationToken ct = default)
    {
        var value = loginId.Trim();

        return await _db.Teachers.AnyAsync(
            t => t.LoginId == value && (excludingTeacherId == null || t.Id != excludingTeacherId), ct);
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<Result<int>> CreateAsync(
        TeacherSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var loginId = request.LoginId!.Trim();

        if (await LoginIdExistsAsync(loginId, null, ct))
            return Result<int>.Failure($"User id '{loginId}' is already taken.", ResultStatus.Conflict);

        var teacher = new Teacher
        {
            Name = request.Name?.Trim(),
            LoginId = loginId,
            Phone = request.Phone?.Trim(),
            PersonalEmail = string.IsNullOrWhiteSpace(request.PersonalEmail) ? null : request.PersonalEmail.Trim(),
            Designation = Blank(request.Designation),
            Qualification = Blank(request.Qualification),
            ComputerTraining = Blank(request.ComputerTraining),
            Branch = Blank(request.Branch),
            IsTechnical = request.IsTechnical,
            CreatedBy = actor,
            CreatedOn = DateTime.UtcNow
        };

        _db.Teachers.Add(teacher);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Teacher {TeacherId} ({Name}) created by {Actor}", teacher.Id, teacher.Name, actor);
        return Result<int>.Success(teacher.Id);
    }

    public async Task<Result<bool>> UpdateAsync(
        int id, TeacherSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var teacher = await _db.Teachers.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (teacher is null)
            return Result<bool>.Failure("Teacher not found.", ResultStatus.NotFound);

        var loginId = request.LoginId!.Trim();

        if (await LoginIdExistsAsync(loginId, id, ct))
            return Result<bool>.Failure($"User id '{loginId}' belongs to another teacher.", ResultStatus.Conflict);

        teacher.Name = request.Name?.Trim();
        teacher.LoginId = loginId;
        teacher.Phone = request.Phone?.Trim();
        teacher.PersonalEmail = string.IsNullOrWhiteSpace(request.PersonalEmail) ? null : request.PersonalEmail.Trim();
        teacher.Designation = Blank(request.Designation);
        teacher.Qualification = Blank(request.Qualification);
        teacher.ComputerTraining = Blank(request.ComputerTraining);
        teacher.Branch = Blank(request.Branch);
        teacher.IsTechnical = request.IsTechnical;
        teacher.ModifiedBy = actor;
        teacher.ModifiedOn = DateTime.UtcNow;

        // NOTE: Teacher.CreatedBy/ModifiedBy are mapped with Ignore() because
        // dbo.Teacher cannot gain audit columns, so the assignments above are
        // deliberate no-ops kept for the day the schema can hold them.

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// The legacy <c>Delete Teacher where id=...</c> had no guard, which is what
    /// orphaned 53 student references and 527 payout rows in the live data
    /// (MIGRATION_PLAN defect T4). Deletion is refused while anything still points here.
    /// </summary>
    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var teacher = await _db.Teachers.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (teacher is null)
            return Result<bool>.Failure("Teacher not found.", ResultStatus.NotFound);

        var students = await _db.Students.CountAsync(s =>
            s.PrimaryTeacherId == id || s.SecondTeacherId == id || s.ThirdTeacherId == id, ct);

        var payouts = await _db.TeacherFees.CountAsync(f => f.TeacherId == id, ct);
        var expenses = await _db.TeacherExpenses.CountAsync(e => e.TeacherId == id, ct);

        if (students > 0 || payouts > 0 || expenses > 0)
        {
            var reasons = new List<string>();
            if (students > 0) reasons.Add($"{students} student(s)");
            if (payouts > 0) reasons.Add($"{payouts} payout record(s)");
            if (expenses > 0) reasons.Add($"{expenses} expense record(s)");

            return Result<bool>.Failure(
                $"{teacher.Name} cannot be deleted - {string.Join(", ", reasons)} still reference this teacher. " +
                "Reassign them first.",
                ResultStatus.Conflict);
        }

        _db.Teachers.Remove(teacher);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Teacher {TeacherId} deleted (had no references)", id);
        return Result<bool>.Success(true);
    }

    public async Task<PagedResult<TeacherEarningDto>> GetEarningsAsync(
        int teacherId, DateOnly? from, DateOnly? to, PagedQuery query, CancellationToken ct = default)
    {
        var payouts = _db.TeacherFees.AsNoTracking().Where(f => f.TeacherId == teacherId);

        // A teacher who takes no fee share has only 0 rows, kept to hold each
        // bill's teacher count - they are not earnings, so not listed.
        if (await _db.Teachers.AnyAsync(t => t.Id == teacherId && t.NoFeeShare, ct))
            payouts = payouts.Where(f => f.Salary != 0m);

        if (from is { } start) payouts = payouts.Where(f => f.PaidOn >= start);
        if (to is { } end) payouts = payouts.Where(f => f.PaidOn <= end);

        /* Admission referral credits are earnings too, and GetAccountAsync already
           counts them in the total - so they are listed here as their own rows,
           or the list would not add up to the "earned" figure above it. The id is
           negative so a referral row is never mistaken for a TecFees payout. */
        var referrals = _db.ReferralCredits.AsNoTracking()
            .Where(r => r.ReferrerType == ReferrerTypes.Teacher && r.ReferrerId == teacherId);

        if (from is { } rStart) referrals = referrals.Where(r => r.CreditedOn >= rStart);
        if (to is { } rEnd) referrals = referrals.Where(r => r.CreditedOn <= rEnd);

        var payoutRows = await payouts
            .Select(f => new { f.Id, f.PaidOn, f.BillNo, f.StudentRegistrationId, f.Salary, Percent = (decimal?)null })
            .ToListAsync(ct);

        var referralRows = await referrals
            .Select(r => new
            {
                Id = -r.Id,
                PaidOn = (DateOnly?)r.CreditedOn,
                BillNo = (string?)"Referral",
                StudentRegistrationId = r.RegistrationId,
                Salary = (decimal?)r.Amount,
                Percent = (decimal?)r.Percent
            })
            .ToListAsync(ct);

        var all = payoutRows.Concat(referralRows)
            .OrderByDescending(f => f.PaidOn).ThenByDescending(f => Math.Abs(f.Id))
            .ToList();

        var totalCount = all.Count;

        var rows = all
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        var regIds = rows
            .Select(r => r.StudentRegistrationId)
            .Where(r => r != null)
            .Distinct()
            .ToList();

        var students = await _db.Students
            .AsNoTracking()
            .Where(s => s.RegistrationId != null && regIds.Contains(s.RegistrationId))
            .Select(s => new
            {
                s.RegistrationId,
                s.Name,
                CourseName = s.Course != null ? s.Course.Name : null
            })
            .ToListAsync(ct);

        var items = rows.Select(r =>
        {
            /* Case-INSENSITIVE, and that is not a nicety: dbo.TecFees holds ids in
               mixed case beside the uppercase ones in dbo.student - 3,953 "Avi/..."
               against 3,011 "AVI/...". SQL Server matched them anyway under a CI
               collation; C# string equality is ordinal and does not, so 8,653 of
               11,670 payout rows showed a blank student and course.

               Registration ids are also not unique in the migrated data, so this
               deliberately takes the first match. */
            var student = students.FirstOrDefault(
                s => string.Equals(s.RegistrationId, r.StudentRegistrationId,
                                   StringComparison.OrdinalIgnoreCase));

            return new TeacherEarningDto(
                r.Id, r.PaidOn, r.BillNo, student?.Name, r.StudentRegistrationId,
                r.Percent is { } pct ? $"Admission referral ({pct:0.##}%)" : student?.CourseName,
                r.Salary ?? 0m);
        }).ToList();

        return PagedResult<TeacherEarningDto>.Create(items, query.Page, query.PageSize, totalCount);
    }

    /// <summary>
    /// Fills in student counts and earnings for a page of teachers using two
    /// set-based queries, rather than one pair per row. A teacher is referenced from
    /// three separate columns on Student, so the counting is done in memory.
    /// </summary>
    private async Task<List<TeacherListItemDto>> DecorateAsync(
        List<(int Id, string? Name, string? LoginId, string? Phone, string? PersonalEmail,
              string? Designation, string? Qualification, string? ComputerTraining, string? Branch,
              bool IsTechnical, bool IsPlaceholder)> teachers,
        CancellationToken ct)
    {
        if (teachers.Count == 0) return [];

        var ids = teachers.Select(t => t.Id).ToList();

        var assignments = await _db.Students
            .AsNoTracking()
            .Where(s => (s.PrimaryTeacherId != null && ids.Contains(s.PrimaryTeacherId.Value))
                     || (s.SecondTeacherId != null && ids.Contains(s.SecondTeacherId.Value))
                     || (s.ThirdTeacherId != null && ids.Contains(s.ThirdTeacherId.Value)))
            .Select(s => new { s.PrimaryTeacherId, s.SecondTeacherId, s.ThirdTeacherId, IsActive = s.IsActive == true })
            .ToListAsync(ct);

        var earnings = await _db.TeacherFees
            .AsNoTracking()
            .Where(f => f.TeacherId != null && ids.Contains(f.TeacherId.Value))
            .GroupBy(f => f.TeacherId!.Value)
            .Select(g => new
            {
                TeacherId = g.Key,
                Total = g.Sum(x => (decimal?)x.Salary ?? 0m),
                LastPaid = g.Max(x => x.PaidOn)
            })
            .ToListAsync(ct);

        // Admission referral credits are part of what a teacher has earned.
        var referrals = await _db.ReferralCredits
            .AsNoTracking()
            .Where(r => r.ReferrerType == ReferrerTypes.Teacher && ids.Contains(r.ReferrerId))
            .GroupBy(r => r.ReferrerId)
            .Select(g => new { TeacherId = g.Key, Total = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.TeacherId, x => x.Total, ct);

        return teachers.Select(t =>
        {
            var assigned = assignments
                .Where(s => s.PrimaryTeacherId == t.Id || s.SecondTeacherId == t.Id || s.ThirdTeacherId == t.Id)
                .ToList();

            var money = earnings.FirstOrDefault(e => e.TeacherId == t.Id);

            return new TeacherListItemDto(
                t.Id, t.Name, t.LoginId, t.Phone, t.PersonalEmail,
                t.Designation, t.Qualification, t.ComputerTraining, t.Branch, t.IsTechnical,
                assigned.Count(s => s.IsActive == true),
                assigned.Count,
                (money?.Total ?? 0m) + referrals.GetValueOrDefault(t.Id),
                money?.LastPaid,
                t.IsPlaceholder,
                assigned.Count > 0 || money is not null);
        }).ToList();
    }

    /* ============================= payments made to a teacher (TecExpense) */

    /// <summary>
    /// Records money handed to a teacher.
    /// </summary>
    /// <remarks>
    /// The teacher account report reads earned from <c>TecFees</c> — the teacher's
    /// share of every fee collected — and paid from <c>TecExpense</c>. Only the
    /// earned side was ever written by this application, so a teacher's pending
    /// balance could only grow. This writes the other side.
    /// </remarks>
    public async Task<Result<int>> RecordPaymentAsync(
        TeacherPaymentRequest request, string? actor, CancellationToken ct = default)
    {
        var teacher = await _db.Teachers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.TeacherId, ct);

        if (teacher is null)
            return Result<int>.Failure("That teacher no longer exists.", ResultStatus.NotFound);

        var payment = new TeacherExpense
        {
            TeacherId = request.TeacherId,
            // An omitted date means today, the way the fee form defaults its picker.
            ExpenseDate = request.PaidOn ?? DateOnly.FromDateTime(DateTime.Now),
            Amount = request.Amount,
            CreatedBy = actor,
            CreatedOn = DateTime.Now
        };

        _db.TeacherExpenses.Add(payment);
        await _db.SaveChangesAsync(ct);

        return Result<int>.Success(payment.Id);
    }

    public async Task<Result<IReadOnlyList<TeacherPaymentDto>>> GetPaymentsAsync(
        int teacherId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var q = _db.TeacherExpenses.AsNoTracking().Where(t => t.TeacherId == teacherId);

        if (from.HasValue) q = q.Where(t => t.ExpenseDate >= from);
        if (to.HasValue) q = q.Where(t => t.ExpenseDate <= to);

        var rows = await q
            .OrderByDescending(t => t.ExpenseDate)
            .ThenByDescending(t => t.Id)
            // dbo.TecExpense cannot carry audit columns - the legacy application
            // bare-inserts into it - so who recorded the payment is not known. The
            // date it was paid, which is what anyone actually asks, is ExpenseDate.
            .Select(t => new TeacherPaymentDto(
                t.Id, t.ExpenseDate, t.Amount ?? 0m, null, default))
            .ToListAsync(ct);

        return Result<IReadOnlyList<TeacherPaymentDto>>.Success(rows);
    }

    /// <summary>
    /// Earned, paid and pending for one teacher over a date range.
    /// </summary>
    /// <remarks>
    /// Scoped to the same dates as the earnings list beneath it. Showing all-time
    /// figures above a filtered list invites the two to be read together — "earned
    /// ₹5.3 lakh" over a heading that says August — and the pending figure would be
    /// the lifetime balance rather than anything to do with the month on screen.
    /// </remarks>
    public async Task<Result<TeacherAccountDto>> GetAccountAsync(
        int teacherId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var teacher = await _db.Teachers
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == teacherId, ct);

        if (teacher is null)
            return Result<TeacherAccountDto>.Failure("That teacher no longer exists.", ResultStatus.NotFound);

        // Summed in SQL, so the figures do not depend on how many rows a screen
        // happens to have drawn.
        var earnedQuery = _db.TeacherFees.AsNoTracking().Where(t => t.TeacherId == teacherId);
        var paidQuery = _db.TeacherExpenses.AsNoTracking().Where(t => t.TeacherId == teacherId);

        if (from.HasValue)
        {
            earnedQuery = earnedQuery.Where(t => t.PaidOn >= from);
            paidQuery = paidQuery.Where(t => t.ExpenseDate >= from);
        }

        if (to.HasValue)
        {
            earnedQuery = earnedQuery.Where(t => t.PaidOn <= to);
            paidQuery = paidQuery.Where(t => t.ExpenseDate <= to);
        }

        var earned = await earnedQuery.SumAsync(t => (decimal?)t.Salary ?? 0m, ct);

        // Plus admission referral credits, over the same date range.
        var referralQuery = _db.ReferralCredits.AsNoTracking()
            .Where(r => r.ReferrerType == ReferrerTypes.Teacher && r.ReferrerId == teacherId);
        if (from.HasValue) referralQuery = referralQuery.Where(r => r.CreditedOn >= from);
        if (to.HasValue) referralQuery = referralQuery.Where(r => r.CreditedOn <= to);
        earned += await referralQuery.SumAsync(r => (decimal?)r.Amount ?? 0m, ct);
        var paid = await paidQuery.SumAsync(t => (decimal?)t.Amount ?? 0m, ct);

        return Result<TeacherAccountDto>.Success(new TeacherAccountDto(
            teacherId, teacher.Name, earned, paid, earned - paid,
            from, to, from.HasValue || to.HasValue));
    }

    public async Task<Result<bool>> DeletePaymentAsync(int paymentId, CancellationToken ct = default)
    {
        var payment = await _db.TeacherExpenses.FirstOrDefaultAsync(t => t.Id == paymentId, ct);

        if (payment is null)
            return Result<bool>.Failure("That payment no longer exists.", ResultStatus.NotFound);

        _db.TeacherExpenses.Remove(payment);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
