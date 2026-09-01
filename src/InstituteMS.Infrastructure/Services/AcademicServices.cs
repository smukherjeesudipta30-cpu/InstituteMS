using InstituteMS.Application.Academics;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Course master and the course-duration report. Replaces <c>CourseController</c> +
/// <c>Models/Course.cs</c> + <c>Models/CourseDuration.cs</c> (MIGRATION_PLAN §4.2, §5.3).
/// </summary>
public class CourseService : ICourseService
{
    private readonly AppDbContext _db;
    private readonly ILogger<CourseService> _logger;

    public CourseService(AppDbContext db, ILogger<CourseService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<CourseListItemDto>> GetListAsync(
        PagedQuery query, CancellationToken ct = default)
    {
        var courses = _db.Courses.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            courses = courses.Where(c => c.Name != null && c.Name.Contains(term));
        }

        var totalCount = await courses.CountAsync(ct);

        var items = await courses
            .OrderBy(c => c.Name)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(c => new CourseListItemDto(
                c.Cid,
                c.Name,
                c.Duration,
                c.Students.Count(s => s.IsActive == true),
                c.Students.Count,
                c.Students.Any()))
            .ToListAsync(ct);

        return PagedResult<CourseListItemDto>.Create(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<bool> NameExistsAsync(
        string name, int? excludingCourseId, CancellationToken ct = default)
    {
        var value = name.Trim();

        return await _db.Courses.AnyAsync(
            c => c.Name == value && (excludingCourseId == null || c.Cid != excludingCourseId), ct);
    }

    public async Task<Result<int>> CreateAsync(
        CourseSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var name = request.Name!.Trim();

        if (await NameExistsAsync(name, null, ct))
            return Result<int>.Failure($"A course called '{name}' already exists.", ResultStatus.Conflict);

        // The legacy insert was positional — "Insert into Course values (...)" — which
        // breaks silently the moment a column is added (defect D6).
        var course = new Course
        {
            Name = name,
            Duration = request.Duration,
            CreatedBy = actor,
            CreatedOn = DateTime.UtcNow
        };

        _db.Courses.Add(course);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Course {CourseId} ({Name}) created by {Actor}", course.Cid, name, actor);
        return Result<int>.Success(course.Cid);
    }

    public async Task<Result<bool>> UpdateAsync(
        int courseId, CourseSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Cid == courseId, ct);
        if (course is null)
            return Result<bool>.Failure("Course not found.", ResultStatus.NotFound);

        var name = request.Name!.Trim();

        if (await NameExistsAsync(name, courseId, ct))
            return Result<bool>.Failure($"A course called '{name}' already exists.", ResultStatus.Conflict);

        course.Name = name;
        course.Duration = request.Duration;
        course.ModifiedBy = actor;
        course.ModifiedOn = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// The legacy <c>Delete Course where Cid=...</c> had no guard, so deleting a
    /// course left its students pointing at nothing. Plan §4.2 C4 calls for the
    /// referential check added here.
    /// </summary>
    public async Task<Result<bool>> DeleteAsync(int courseId, CancellationToken ct = default)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Cid == courseId, ct);
        if (course is null)
            return Result<bool>.Failure("Course not found.", ResultStatus.NotFound);

        var students = await _db.Students.CountAsync(s => s.CourseId == courseId, ct);

        if (students > 0)
        {
            return Result<bool>.Failure(
                $"'{course.Name}' cannot be deleted - {students} student(s) are enrolled on it. " +
                "Move them to another course first.",
                ResultStatus.Conflict);
        }

        _db.Courses.Remove(course);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Course {CourseId} deleted (no students enrolled)", courseId);
        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Per-student course progress. The legacy query used
    /// <c>ROW_NUMBER() OVER (...)</c> purely to number the rows on screen; that is a
    /// client concern, so the student id is returned instead (plan §4.1 S12).
    /// </summary>
    public async Task<PagedResult<CourseProgressDto>> GetProgressAsync(
        CourseProgressQuery query, CancellationToken ct = default)
    {
        var students = _db.Students.AsNoTracking().Where(s => s.IsActive == true);

        if (query.CourseId is { } courseId)
            students = students.Where(s => s.CourseId == courseId);

        if (!string.IsNullOrWhiteSpace(query.RegistrationId))
        {
            var reg = query.RegistrationId.Trim();
            students = students.Where(s => s.RegistrationId != null && s.RegistrationId.Contains(reg));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            students = students.Where(s =>
                (s.Name != null && s.Name.Contains(term)) ||
                (s.RegistrationId != null && s.RegistrationId.Contains(term)));
        }

        var totalCount = await students.CountAsync(ct);

        var rows = await students
            .OrderByDescending(s => s.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.RegistrationId,
                s.Phone,
                s.Email,
                s.DateOfJoining,
                CourseName = s.Course != null ? s.Course.Name : null,
                TotalMonths = s.Course != null ? s.Course.Duration : null
            })
            .ToListAsync(ct);

        var regIds = rows
            .Select(r => r.RegistrationId)
            .Where(r => !string.IsNullOrEmpty(r))
            .Distinct()
            .ToList();

        // Months paid = number of fee-ledger rows for the student.
        var paid = await _db.MonthlyFees
            .AsNoTracking()
            .Where(f => f.StudentRegistrationId != null && regIds.Contains(f.StudentRegistrationId))
            .GroupBy(f => f.StudentRegistrationId!)
            .Select(g => new { RegistrationId = g.Key, Months = g.Count() })
            .ToListAsync(ct);

        var items = rows.Select(r =>
        {
            var total = r.TotalMonths ?? 0;

            var paidMonths = r.RegistrationId is null
                ? 0
                // Case-insensitive: mixed-case ids in the ledger would otherwise
                // report every paid month as zero.
                : paid.FirstOrDefault(
                      p => string.Equals(p.RegistrationId, r.RegistrationId,
                                         StringComparison.OrdinalIgnoreCase))?.Months ?? 0;

            // A student can pay past the nominal course length, so the remaining
            // figure is floored at zero rather than going negative.
            var remaining = Math.Max(0, total - paidMonths);

            var percent = total <= 0
                ? 0
                : (int)Math.Min(100, Math.Round(paidMonths * 100.0 / total, MidpointRounding.AwayFromZero));

            return new CourseProgressDto(
                r.Id, r.Name, r.RegistrationId, r.Phone, r.Email, r.DateOfJoining,
                r.CourseName, total, paidMonths, remaining, percent);
        }).ToList();

        return PagedResult<CourseProgressDto>.Create(items, query.Page, query.PageSize, totalCount);
    }
}

/// <summary>
/// Exam marks. Replaces <c>MarksController</c> + <c>Models/Mark.cs</c>
/// (MIGRATION_PLAN §4.5, §5.6).
///
/// <para>
/// Written with EF Core rather than through <c>usp_InsertMarkData</c> /
/// <c>usp_UpdateMarkData</c>: those do a positional
/// <c>Insert into Marks values(...)</c>, which silently writes to the wrong columns
/// the moment one is added.
/// </para>
/// </summary>
public class MarkService : IMarkService
{
    private readonly AppDbContext _db;
    private readonly ILogger<MarkService> _logger;

    public MarkService(AppDbContext db, ILogger<MarkService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<MarkListItemDto>> GetListAsync(
        MarkQuery query, CancellationToken ct = default)
    {
        var marks = _db.Marks.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.RegistrationId))
        {
            var reg = query.RegistrationId.Trim();
            marks = marks.Where(m => m.StudentRegistrationId != null && m.StudentRegistrationId.Contains(reg));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            marks = marks.Where(m =>
                (m.StudentRegistrationId != null && m.StudentRegistrationId.Contains(term)) ||
                (m.Subject1 != null && m.Subject1.Contains(term)) ||
                (m.Subject2 != null && m.Subject2.Contains(term)) ||
                (m.Subject3 != null && m.Subject3.Contains(term)) ||
                (m.Subject4 != null && m.Subject4.Contains(term)));
        }

        var totalCount = await marks.CountAsync(ct);

        var rows = await marks
            .OrderByDescending(m => m.ExamDate).ThenByDescending(m => m.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        var items = await ToDtosAsync(rows, ct);

        return PagedResult<MarkListItemDto>.Create(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<Result<MarkListItemDto>> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var mark = await _db.Marks.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mark is null)
            return Result<MarkListItemDto>.Failure("Marks record not found.", ResultStatus.NotFound);

        var items = await ToDtosAsync([mark], ct);
        return Result<MarkListItemDto>.Success(items[0]);
    }

    public async Task<Result<int>> CreateAsync(
        MarkSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var reg = request.RegistrationId!.Trim();

        if (!await _db.Students.AnyAsync(s => s.RegistrationId == reg, ct))
            return Result<int>.Failure($"No student found with registration id '{reg}'.", ResultStatus.NotFound);

        var mark = new Mark
        {
            CreatedBy = actor,
            CreatedOn = DateTime.UtcNow
        };

        Apply(request, mark);

        _db.Marks.Add(mark);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Marks {MarkId} recorded for {RegistrationId} by {Actor}", mark.Id, reg, actor);
        return Result<int>.Success(mark.Id);
    }

    public async Task<Result<bool>> UpdateAsync(
        int id, MarkSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var mark = await _db.Marks.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mark is null)
            return Result<bool>.Failure("Marks record not found.", ResultStatus.NotFound);

        var reg = request.RegistrationId!.Trim();

        if (!await _db.Students.AnyAsync(s => s.RegistrationId == reg, ct))
            return Result<bool>.Failure($"No student found with registration id '{reg}'.", ResultStatus.NotFound);

        Apply(request, mark);
        mark.ModifiedBy = actor;
        mark.ModifiedOn = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var mark = await _db.Marks.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mark is null)
            return Result<bool>.Failure("Marks record not found.", ResultStatus.NotFound);

        _db.Marks.Remove(mark);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    private static void Apply(MarkSaveRequest request, Mark mark)
    {
        mark.StudentRegistrationId = request.RegistrationId?.Trim();
        mark.ExamDate = request.ExamDate;

        // An empty subject clears its marks too, so a cleared slot cannot leave
        // orphaned numbers behind.
        (mark.Subject1, mark.FullMarks1, mark.ObtainedMarks1) =
            Slot(request.Subject1, request.FullMarks1, request.ObtainedMarks1);

        (mark.Subject2, mark.FullMarks2, mark.ObtainedMarks2) =
            Slot(request.Subject2, request.FullMarks2, request.ObtainedMarks2);

        (mark.Subject3, mark.FullMarks3, mark.ObtainedMarks3) =
            Slot(request.Subject3, request.FullMarks3, request.ObtainedMarks3);

        (mark.Subject4, mark.FullMarks4, mark.ObtainedMarks4) =
            Slot(request.Subject4, request.FullMarks4, request.ObtainedMarks4);
    }

    private static (string?, int?, int?) Slot(string? subject, int? full, int? obtained) =>
        string.IsNullOrWhiteSpace(subject) ? (null, null, null) : (subject.Trim(), full, obtained);

    /// <summary>Attaches student names and computes the totals across filled slots.</summary>
    private async Task<List<MarkListItemDto>> ToDtosAsync(List<Mark> marks, CancellationToken ct)
    {
        if (marks.Count == 0) return [];

        var regIds = marks
            .Select(m => m.StudentRegistrationId)
            .Where(r => !string.IsNullOrEmpty(r))
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

        return marks.Select(m =>
        {
            var subjects = new List<MarkSubjectDto>();

            void Add(string? subject, int? full, int? obtained)
            {
                if (!string.IsNullOrWhiteSpace(subject))
                    subjects.Add(new MarkSubjectDto(subject, full, obtained));
            }

            Add(m.Subject1, m.FullMarks1, m.ObtainedMarks1);
            Add(m.Subject2, m.FullMarks2, m.ObtainedMarks2);
            Add(m.Subject3, m.FullMarks3, m.ObtainedMarks3);
            Add(m.Subject4, m.FullMarks4, m.ObtainedMarks4);

            var totalFull = subjects.Sum(s => s.FullMarks ?? 0);
            var totalObtained = subjects.Sum(s => s.ObtainedMarks ?? 0);

            var percent = totalFull <= 0
                ? 0m
                : decimal.Round(totalObtained * 100m / totalFull, 2, MidpointRounding.AwayFromZero);

            /* Case-insensitive for the same reason as the teacher payout list: the
               ledger holds ids in mixed case and C# equality is ordinal, so an
               exact match leaves the student and course blank. Ids are also not
               unique in the migrated data, so the first match is taken. */
            var student = students.FirstOrDefault(
                s => string.Equals(s.RegistrationId, m.StudentRegistrationId,
                                   StringComparison.OrdinalIgnoreCase));

            return new MarkListItemDto(
                m.Id, m.StudentRegistrationId, student?.Name, student?.CourseName,
                m.ExamDate, subjects, totalFull, totalObtained, percent);
        }).ToList();
    }
}

/// <summary>
/// Notice board. Replaces <c>NoticeController</c> + <c>Models/Notice.cs</c>
/// (MIGRATION_PLAN §4.7 N1).
/// </summary>
public class NoticeService : INoticeService
{
    private readonly AppDbContext _db;
    private readonly ILogger<NoticeService> _logger;

    public NoticeService(AppDbContext db, ILogger<NoticeService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<NoticeListItemDto>> GetListAsync(
        PagedQuery query, CancellationToken ct = default)
    {
        var notices = _db.Notices.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            notices = notices.Where(n => n.Text != null && n.Text.Contains(term));
        }

        var totalCount = await notices.CountAsync(ct);

        var items = await notices
            .OrderByDescending(n => n.NoticeDate).ThenByDescending(n => n.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(n => new NoticeListItemDto(n.Id, n.Text, n.NoticeDate))
            .ToListAsync(ct);

        return PagedResult<NoticeListItemDto>.Create(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<Result<int>> CreateAsync(
        NoticeSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var notice = new Notice
        {
            Text = request.Text?.Trim(),
            // The legacy controller always stamped DateTime.Now; an explicit date is
            // accepted here so a notice can be backdated or scheduled.
            NoticeDate = request.NoticeDate ?? DateTime.Now,
            CreatedBy = actor,
            CreatedOn = DateTime.UtcNow
        };

        _db.Notices.Add(notice);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Notice {NoticeId} published by {Actor}", notice.Id, actor);
        return Result<int>.Success(notice.Id);
    }

    public async Task<Result<bool>> UpdateAsync(
        int id, NoticeSaveRequest request, string? actor, CancellationToken ct = default)
    {
        var notice = await _db.Notices.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (notice is null)
            return Result<bool>.Failure("Notice not found.", ResultStatus.NotFound);

        notice.Text = request.Text?.Trim();

        // The legacy update overwrote the date with "now" on every edit, so fixing a
        // typo silently moved the notice to the top of the board. The original date
        // is kept unless a new one is supplied.
        if (request.NoticeDate is { } date) notice.NoticeDate = date;

        notice.ModifiedBy = actor;
        notice.ModifiedOn = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default)
    {
        var notice = await _db.Notices.FirstOrDefaultAsync(n => n.Id == id, ct);
        if (notice is null)
            return Result<bool>.Failure("Notice not found.", ResultStatus.NotFound);

        _db.Notices.Remove(notice);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
