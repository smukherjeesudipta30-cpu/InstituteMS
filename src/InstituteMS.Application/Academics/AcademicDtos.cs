using InstituteMS.Application.Common;

namespace InstituteMS.Application.Academics;

/* -------------------------------------------------------------------- courses */

public record CourseListItemDto(
    int CourseId,
    string? Name,
    /// <summary>Course length in months.</summary>
    int? Duration,
    int ActiveStudents,
    int TotalStudents,
    /// <summary>False when nothing references the course, so it can be deleted.</summary>
    bool IsReferenced);

public record CourseSaveRequest
{
    public string? Name { get; init; }
    public int? Duration { get; init; }
}

/// <summary>
/// One student's progress through their course (legacy <c>CourseDuration</c>).
/// <c>PaidMonths</c> is the count of fee-ledger rows, so it is months paid for
/// rather than months elapsed.
/// </summary>
public record CourseProgressDto(
    int StudentId,
    string? StudentName,
    string? RegistrationId,
    string? Phone,
    string? Email,
    DateOnly? DateOfJoining,
    string? CourseName,
    int TotalMonths,
    int PaidMonths,
    int RemainingMonths,
    /// <summary>Paid as a percentage of the course length; 0 when the length is unknown.</summary>
    int PercentComplete);

public class CourseProgressQuery : PagedQuery
{
    public int? CourseId { get; set; }
    public string? RegistrationId { get; set; }
}

/* ---------------------------------------------------------------------- marks */

public record MarkSubjectDto(string? Subject, int? FullMarks, int? ObtainedMarks);

public record MarkListItemDto(
    int Id,
    string? RegistrationId,
    string? StudentName,
    string? CourseName,
    DateOnly? ExamDate,
    IReadOnlyList<MarkSubjectDto> Subjects,
    /// <summary>Totals across the subjects that were filled in.</summary>
    int TotalFullMarks,
    int TotalObtainedMarks,
    decimal PercentScored,
    // "Register" — typed on the Marks screen, editable here. "ExamPortal" — a
    // theory sat online and/or a practical entered on the Exams screen; shown
    // read-only here (its id is negative), corrected from the Exams screen.
    string Source = "Register");

public record MarkSaveRequest
{
    public string? RegistrationId { get; init; }
    public DateOnly? ExamDate { get; init; }

    public string? Subject1 { get; init; }
    public int? FullMarks1 { get; init; }
    public int? ObtainedMarks1 { get; init; }

    public string? Subject2 { get; init; }
    public int? FullMarks2 { get; init; }
    public int? ObtainedMarks2 { get; init; }

    public string? Subject3 { get; init; }
    public int? FullMarks3 { get; init; }
    public int? ObtainedMarks3 { get; init; }

    public string? Subject4 { get; init; }
    public int? FullMarks4 { get; init; }
    public int? ObtainedMarks4 { get; init; }
}

public class MarkQuery : PagedQuery
{
    public string? RegistrationId { get; set; }
}

/* -------------------------------------------------------------------- notices */

public record NoticeListItemDto(int Id, string? Text, DateTime? NoticeDate);

public record NoticeSaveRequest
{
    public string? Text { get; init; }
    /// <summary>Optional. Defaults to now, as the legacy insert did.</summary>
    public DateTime? NoticeDate { get; init; }
}

/* ------------------------------------------------------------------ contracts */

public interface ICourseService
{
    Task<PagedResult<CourseListItemDto>> GetListAsync(PagedQuery query, CancellationToken ct = default);

    /// <summary>Legacy <c>ufnGetCourseChk</c> — true when the name is already taken.</summary>
    Task<bool> NameExistsAsync(string name, int? excludingCourseId, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(CourseSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(int courseId, CourseSaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Refuses to delete a course any student is enrolled on (plan §4.2 C4).</summary>
    Task<Result<bool>> DeleteAsync(int courseId, CancellationToken ct = default);

    Task<PagedResult<CourseProgressDto>> GetProgressAsync(
        CourseProgressQuery query, CancellationToken ct = default);
}

public interface IMarkService
{
    Task<PagedResult<MarkListItemDto>> GetListAsync(MarkQuery query, CancellationToken ct = default);

    Task<Result<MarkListItemDto>> GetByIdAsync(int id, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(MarkSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(int id, MarkSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);
}

public interface INoticeService
{
    Task<PagedResult<NoticeListItemDto>> GetListAsync(PagedQuery query, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(NoticeSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(int id, NoticeSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> DeleteAsync(int id, CancellationToken ct = default);
}
