using InstituteMS.Application.Academics;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Course master and the course-duration report. Replaces <c>CourseController</c> +
/// <c>CourseDurationController</c> (MIGRATION_PLAN §5.3).
/// </summary>
[Route("api/v1/courses")]
[Tags("Courses")]
// Receptionist is admitted at the class level only so that the progress report
// below can accept them; every other action re-states BackOffice, and ASP.NET Core
// requires *all* [Authorize] attributes on the path to pass.
[Authorize(Roles = UserRoles.MasterData)]
public class CoursesController : ApiControllerBase
{
    private readonly ICourseService _courses;

    public CoursesController(ICourseService courses) => _courses = courses;

    /// <summary>Paged list with enrolment counts.</summary>
    [HttpGet]
    [Authorize(Roles = UserRoles.MasterData)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CourseListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PagedQuery query, CancellationToken ct)
        => Envelope(await _courses.GetListAsync(query, ct));

    /// <summary>Legacy <c>CourseCheck</c> / <c>ufnGetCourseChk</c>, for live form feedback.</summary>
    [HttpGet("name-exists")]
    [Authorize(Roles = UserRoles.MasterData)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> NameExists(
        [FromQuery] string name, [FromQuery] int? excludingCourseId, CancellationToken ct)
        => Envelope(await _courses.NameExistsAsync(name, excludingCourseId, ct));

    /// <summary>
    /// Per-student course progress: total months, months paid and months remaining
    /// (legacy <c>CourseDuration/AllCourses</c>, <c>CoursesByCid</c>,
    /// <c>CoursesByCidAndStuRegId</c> — three actions collapsed into one filtered query).
    /// </summary>
    /// <remarks>
    /// A Receptionist is allowed here because the legacy
    /// <c>CourseDuration/ByCourse</c>, <c>CoursesByCid</c> and
    /// <c>CoursesByCidAndStuRegId</c> carried
    /// <c>[Authorize(Roles = "Admin,Editor,Receptionist")]</c> — but
    /// <c>CourseDuration/Index</c>, the unfiltered listing of every active student,
    /// was <c>Admin,Editor</c> only. Since the three collapse into this one endpoint,
    /// a Receptionist must name a course or a registration id; without a filter they
    /// would gain the whole-institute view the legacy project withheld from them.
    /// </remarks>
    [HttpGet("progress")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CourseProgressDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CourseProgressDto>>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Progress([FromQuery] CourseProgressQuery query, CancellationToken ct)
    {
        if (!IsBackOffice && query.CourseId is not > 0 && string.IsNullOrWhiteSpace(query.RegistrationId))
        {
            return Envelope(Result<PagedResult<CourseProgressDto>>.Failure(
                "Choose a course or enter a registration id to run this report.",
                ResultStatus.Forbidden));
        }

        return Envelope(await _courses.GetProgressAsync(query, ct));
    }

    [HttpPost]
    [Authorize(Roles = UserRoles.MasterData)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CourseSaveRequest request, CancellationToken ct)
        => Envelope(await _courses.CreateAsync(request, CurrentUserName, ct), "Course saved.");

    [HttpPut("{courseId:int}")]
    [Authorize(Roles = UserRoles.MasterData)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int courseId, [FromBody] CourseSaveRequest request, CancellationToken ct)
        => Envelope(await _courses.UpdateAsync(courseId, request, CurrentUserName, ct), "Course updated.");

    /// <summary>
    /// Deletes a course, but only when no student is enrolled on it. The legacy
    /// delete had no such guard (plan §4.2 C4).
    /// </summary>
    [HttpDelete("{courseId:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int courseId, CancellationToken ct)
        => Envelope(await _courses.DeleteAsync(courseId, ct), "Course deleted.");
}

/// <summary>
/// Exam marks. Replaces <c>MarksController</c> + <c>Views/Marks/Index.cshtml</c>
/// + <c>Scripts/Marks.js</c> (MIGRATION_PLAN §5.6).
/// </summary>
[Route("api/v1/marks")]
[Tags("Marks")]
[Authorize(Roles = UserRoles.MasterData)]
public class MarksController : ApiControllerBase
{
    private readonly IMarkService _marks;

    public MarksController(IMarkService marks) => _marks = marks;

    /// <summary>Paged list. <c>?registrationId=</c> narrows it to one student.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MarkListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] MarkQuery query, CancellationToken ct)
        => Envelope(await _marks.GetListAsync(query, ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<MarkListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Envelope(await _marks.GetByIdAsync(id, ct));

    /// <summary>
    /// Records marks for up to four subjects. Each slot is optional, but a slot with
    /// a subject must carry consistent full and obtained marks — the legacy form
    /// validated neither, so obtained could exceed full.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create([FromBody] MarkSaveRequest request, CancellationToken ct)
        => Envelope(await _marks.CreateAsync(request, CurrentUserName, ct), "Marks saved.");

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] MarkSaveRequest request, CancellationToken ct)
        => Envelope(await _marks.UpdateAsync(id, request, CurrentUserName, ct), "Marks updated.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _marks.DeleteAsync(id, ct), "Marks deleted.");
}

/// <summary>
/// Notice board. Replaces <c>NoticeController</c> + <c>Views/Notice/Index.cshtml</c>
/// (MIGRATION_PLAN §5.9).
/// </summary>
[Route("api/v1/notices")]
[Tags("Notices")]
[Authorize(Roles = UserRoles.MasterData)]
public class NoticesController : ApiControllerBase
{
    private readonly INoticeService _notices;

    public NoticesController(INoticeService notices) => _notices = notices;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<NoticeListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PagedQuery query, CancellationToken ct)
        => Envelope(await _notices.GetListAsync(query, ct));

    /// <summary>Server time, for the board header (legacy <c>gettime</c>).</summary>
    [HttpGet("server-time")]
    [ProducesResponseType(typeof(ApiResponse<DateTime>), StatusCodes.Status200OK)]
    public IActionResult ServerTime() => Envelope(DateTime.Now);

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create([FromBody] NoticeSaveRequest request, CancellationToken ct)
        => Envelope(await _notices.CreateAsync(request, CurrentUserName, ct), "Notice published.");

    /// <summary>
    /// Updates a notice. Its date is left alone unless a new one is supplied — the
    /// legacy update reset it to "now", so correcting a typo bumped the notice back
    /// to the top of the board.
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] NoticeSaveRequest request, CancellationToken ct)
        => Envelope(await _notices.UpdateAsync(id, request, CurrentUserName, ct), "Notice updated.");

    [HttpDelete("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _notices.DeleteAsync(id, ct), "Notice deleted.");
}
