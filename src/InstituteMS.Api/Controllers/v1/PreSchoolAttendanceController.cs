using InstituteMS.Application.Attendance;
using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Attendance for the PreSchool's receptionist and teachers, the same way the
/// Computer Centre takes it: the code on the PreSchool screen or a button on
/// their own phone, inside the PreSchool's fence, confirmed by an emailed code.
/// No Computer Centre role is listed, and the Computer Centre's attendance
/// endpoints do not list a PreSchool one.
/// </summary>
[Route("api/v1/preschool/attendance")]
[Tags("PreSchool")]
[Authorize(Roles = UserRoles.PreSchoolStaff)]
public class PreSchoolAttendanceController : ApiControllerBase
{
    /// <summary>Who marks their own attendance: the receptionist and the teachers.</summary>
    private const string Markers = $"{UserRoles.PreSchoolFrontDesk},{UserRoles.PreSchoolTutor}";

    private readonly IPreSchoolAttendanceService _attendance;

    public PreSchoolAttendanceController(IPreSchoolAttendanceService attendance) => _attendance = attendance;

    /// <summary>The code to show on the PreSchool's screen; it changes every couple of minutes.</summary>
    [HttpGet("qr")]
    [ProducesResponseType(typeof(ApiResponse<AttendanceQrDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Qr(CancellationToken ct) => Envelope(await _attendance.GetQrAsync(ct));

    [HttpPost("scan")]
    [Authorize(Roles = Markers)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceChallengeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Scan([FromBody] AttendanceScanRequest request, CancellationToken ct)
        => Envelope(await _attendance.ScanAsync(CurrentUserName ?? string.Empty, request, ct));

    [HttpPost("mark")]
    [Authorize(Roles = Markers)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceChallengeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mark([FromBody] AttendanceMarkRequest request, CancellationToken ct)
        => Envelope(await _attendance.MarkAsync(CurrentUserName ?? string.Empty, request, ct));

    [HttpPost("confirm")]
    [Authorize(Roles = Markers)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceScanResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Confirm([FromBody] AttendanceConfirmRequest request, CancellationToken ct)
        => Envelope(await _attendance.ConfirmAsync(CurrentUserName ?? string.Empty, request, ct));

    [HttpPost("resend/{challengeId:int}")]
    [Authorize(Roles = Markers)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceChallengeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resend(int challengeId, CancellationToken ct)
        => Envelope(await _attendance.ResendAsync(CurrentUserName ?? string.Empty, challengeId, ct));

    /// <summary>The signed-in receptionist's or teacher's own day.</summary>
    [HttpGet("me")]
    [Authorize(Roles = Markers)]
    [ProducesResponseType(typeof(ApiResponse<MyAttendanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me([FromQuery] DateOnly? onDate, CancellationToken ct)
        => Envelope(await _attendance.GetMyDayAsync(CurrentUserName ?? string.Empty, onDate, ct));

    /// <summary>Everyone's attendance between two dates. The PreSchool admin only.</summary>
    [HttpGet("report")]
    [Authorize(Roles = UserRoles.PreSchoolHead)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolAttendanceReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Report([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int? staffId, CancellationToken ct)
        => Envelope(await _attendance.GetReportAsync(from, to, staffId, ct));
}
