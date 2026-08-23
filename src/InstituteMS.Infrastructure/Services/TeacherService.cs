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
    private const string PlaceholderMarker = "migration-004-orphan";

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

        if (!query.IncludePlaceholders)
            teachers = teachers.Where(t => t.CreatedBy != PlaceholderMarker);

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
            .OrderBy(t => t.CreatedBy == PlaceholderMarker ? 1 : 0)   // stand-ins last
            .ThenBy(t => t.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.LoginId,
                t.Phone,
                IsPlaceholder = t.CreatedBy == PlaceholderMarker
            })
            .ToListAsync(ct);

        var items = await DecorateAsync(page.Select(t =>
            (t.Id, t.Name, t.LoginId, t.Phone, t.IsPlaceholder)).ToList(), ct);

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
                IsPlaceholder = t.CreatedBy == PlaceholderMarker
            })
            .FirstOrDefaultAsync(ct);

        if (teacher is null)
            return Result<TeacherListItemDto>.Failure("Teacher not found.", ResultStatus.NotFound);

        var decorated = await DecorateAsync(
            [(teacher.Id, teacher.Name, teacher.LoginId, teacher.Phone, teacher.IsPlaceholder)], ct);

        return Result<TeacherListItemDto>.Success(decorated[0]);
    }

    public async Task<bool> LoginIdExistsAsync(
        string loginId, int? excludingTeacherId, CancellationToken ct = default)
    {
        var value = loginId.Trim();

        return await _db.Teachers.AnyAsync(
            t => t.LoginId == value && (excludingTeacherId == null || t.Id != excludingTeacherId), ct);
    }

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
        teacher.ModifiedBy = actor;
        teacher.ModifiedOn = DateTime.UtcNow;

        // Editing a stand-in row is how it becomes a real teacher again, so clear the
        // marker once someone has given it a proper name.
        if (teacher.CreatedBy == PlaceholderMarker)
        {
            teacher.CreatedBy = actor;
            _logger.LogInformation(
                "Placeholder teacher {TeacherId} identified as {Name} by {Actor}", id, teacher.Name, actor);
        }

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

        if (from is { } start) payouts = payouts.Where(f => f.PaidOn >= start);
        if (to is { } end) payouts = payouts.Where(f => f.PaidOn <= end);

        var totalCount = await payouts.CountAsync(ct);

        var rows = await payouts
            .OrderByDescending(f => f.PaidOn).ThenByDescending(f => f.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(f => new { f.Id, f.PaidOn, f.BillNo, f.StudentRegistrationId, f.Salary })
            .ToListAsync(ct);

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
            // Registration ids are not unique in the migrated data, so take the first.
            var student = students.FirstOrDefault(s => s.RegistrationId == r.StudentRegistrationId);

            return new TeacherEarningDto(
                r.Id, r.PaidOn, r.BillNo, student?.Name, r.StudentRegistrationId,
                student?.CourseName, r.Salary ?? 0m);
        }).ToList();

        return PagedResult<TeacherEarningDto>.Create(items, query.Page, query.PageSize, totalCount);
    }

    /// <summary>
    /// Fills in student counts and earnings for a page of teachers using two
    /// set-based queries, rather than one pair per row. A teacher is referenced from
    /// three separate columns on Student, so the counting is done in memory.
    /// </summary>
    private async Task<List<TeacherListItemDto>> DecorateAsync(
        List<(int Id, string? Name, string? LoginId, string? Phone, bool IsPlaceholder)> teachers,
        CancellationToken ct)
    {
        if (teachers.Count == 0) return [];

        var ids = teachers.Select(t => t.Id).ToList();

        var assignments = await _db.Students
            .AsNoTracking()
            .Where(s => (s.PrimaryTeacherId != null && ids.Contains(s.PrimaryTeacherId.Value))
                     || (s.SecondTeacherId != null && ids.Contains(s.SecondTeacherId.Value))
                     || (s.ThirdTeacherId != null && ids.Contains(s.ThirdTeacherId.Value)))
            .Select(s => new { s.PrimaryTeacherId, s.SecondTeacherId, s.ThirdTeacherId, s.IsActive })
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

        return teachers.Select(t =>
        {
            var assigned = assignments
                .Where(s => s.PrimaryTeacherId == t.Id || s.SecondTeacherId == t.Id || s.ThirdTeacherId == t.Id)
                .ToList();

            var money = earnings.FirstOrDefault(e => e.TeacherId == t.Id);

            return new TeacherListItemDto(
                t.Id, t.Name, t.LoginId, t.Phone,
                assigned.Count(s => s.IsActive),
                assigned.Count,
                money?.Total ?? 0m,
                money?.LastPaid,
                t.IsPlaceholder,
                assigned.Count > 0 || money is not null);
        }).ToList();
    }
}
