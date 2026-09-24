using InstituteMS.Application.Common;
using InstituteMS.Application.TeacherExams;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The teacher competency exam portal — Admin assigns a teacher to a course,
/// sets a sitting's date/time, AI generates that sitting's questions, and the
/// teacher takes it.
/// </summary>
[Route("api/v1/teacher-exams")]
[Tags("Teacher Exams")]
[Authorize]
public class TeacherExamsController : ApiControllerBase
{
    private readonly ITeacherExamService _exams;

    public TeacherExamsController(ITeacherExamService exams) => _exams = exams;

    /* -------------------------------------------------------- assignments */

    /// <summary>Every standing assignment, with live due-status. Admin/SuperAdmin only.</summary>
    [HttpGet("assignments")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TeacherExamAssignmentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Assignments(CancellationToken ct)
        => Envelope(await _exams.GetAssignmentsAsync(ct));

    [HttpPost("assignments")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAssignment(
        [FromBody] TeacherExamAssignmentSaveRequest request, CancellationToken ct)
        => Envelope(await _exams.CreateAssignmentAsync(request, CurrentUserName, ct), "Teacher assigned to course.");

    [HttpPut("assignments/{id:int}/active")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetAssignmentActive(int id, [FromQuery] bool isActive, CancellationToken ct)
        => Envelope(await _exams.SetAssignmentActiveAsync(id, isActive, CurrentUserName, ct), "Updated.");

    /* ---------------------------------------------------------- schedules */

    /// <summary>
    /// Schedules a sitting and generates its questions. Admin/SuperAdmin only.
    /// </summary>
    [HttpPost("schedules")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Schedule([FromBody] TeacherExamScheduleRequest request, CancellationToken ct)
        => Envelope(await _exams.ScheduleAsync(request, CurrentUserName, ct), "Exam scheduled.");

    /// <summary>Every sitting, for Admin's overview. Admin/SuperAdmin only.</summary>
    [HttpGet("schedules")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TeacherExamScheduleDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Schedules(CancellationToken ct)
        => Envelope(await _exams.GetSchedulesAsync(null, ct));

    /* -------------------------------------------------- the teacher's own */

    /// <summary>The signed-in teacher's own sittings — upcoming, ready and past.</summary>
    [HttpGet("my")]
    [Authorize(Roles = UserRoles.Teacher)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TeacherExamScheduleDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> My(CancellationToken ct)
    {
        var own = await _exams.ResolveOwnTeacherIdAsync(CurrentUserName ?? string.Empty, ct);
        if (!own.Succeeded) return Envelope(own);

        return Envelope(await _exams.GetSchedulesAsync(own.Value, ct));
    }

    /// <summary>
    /// Opens a sitting the signed-in teacher is booked for — refused before
    /// the scheduled time or after it has already been submitted.
    /// </summary>
    [HttpPost("schedules/{scheduleId:int}/start")]
    [Authorize(Roles = UserRoles.Teacher)]
    [ProducesResponseType(typeof(ApiResponse<TeacherExamPaperDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Start(int scheduleId, CancellationToken ct)
    {
        var own = await _exams.ResolveOwnTeacherIdAsync(CurrentUserName ?? string.Empty, ct);
        if (!own.Succeeded) return Envelope(own);

        return Envelope(await _exams.StartAttemptAsync(scheduleId, own.Value, ct));
    }

    /// <summary>Scores the attempt and notifies the teacher, Admin and SuperAdmin.</summary>
    [HttpPost("submit")]
    [Authorize(Roles = UserRoles.Teacher)]
    [ProducesResponseType(typeof(ApiResponse<TeacherExamResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Submit([FromBody] TeacherExamSubmitRequest request, CancellationToken ct)
    {
        var own = await _exams.ResolveOwnTeacherIdAsync(CurrentUserName ?? string.Empty, ct);
        if (!own.Succeeded) return Envelope(own);

        return Envelope(await _exams.SubmitAttemptAsync(request, own.Value, CurrentUserName, ct));
    }

    /// <summary>
    /// Reports one instance of the teacher leaving the exam window — same
    /// full-screen/tab-switch proctoring as the student exam portal.
    /// </summary>
    [HttpPost("violation")]
    [Authorize(Roles = UserRoles.Teacher)]
    [ProducesResponseType(typeof(ApiResponse<TeacherExamViolationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RecordViolation(
        [FromBody] RecordTeacherExamViolationRequest request, CancellationToken ct)
    {
        var own = await _exams.ResolveOwnTeacherIdAsync(CurrentUserName ?? string.Empty, ct);
        if (!own.Succeeded) return Envelope(own);

        return Envelope(await _exams.RecordViolationAsync(request, own.Value, CurrentUserName, ct));
    }
}
