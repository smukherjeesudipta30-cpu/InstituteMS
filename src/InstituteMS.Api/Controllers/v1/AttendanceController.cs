using InstituteMS.Application.Attendance;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Staff attendance by QR scan. A screen at the centre shows a code that changes
/// every minute or so; a teacher or receptionist scans it on their phone, which
/// sends its location, and the scan is kept only if the phone is inside that
/// branch's fence.
/// </summary>
/// <remarks>
/// The first scan of a day is the arrival and the next is the departure, so a lunch
/// break is simply two more scans. Hours come from pairing them. A day with no scan
/// is leave, which needs nothing stored.
/// </remarks>
[Route("api/v1/attendance")]
[Tags("Attendance")]
[Authorize]
public class AttendanceController : ApiControllerBase
{
    /// <summary>Who may stand in front of the code and mark themselves present.</summary>
    private const string Staff = $"{UserRoles.Teacher},{UserRoles.Receptionist}";

    private readonly IAttendanceService _attendance;

    public AttendanceController(IAttendanceService attendance) => _attendance = attendance;

    /// <summary>
    /// The code to display at the centre. Staff may fetch it too: the screen showing
    /// it is as likely to be the receptionist's own browser as the office computer.
    /// </summary>
    [HttpGet("qr")]
    [Authorize(Roles = $"{UserRoles.BackOffice},{Staff}")]
    [ProducesResponseType(typeof(ApiResponse<AttendanceQrDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceQrDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Qr([FromQuery] int? branchId, CancellationToken ct)
        => Envelope(await _attendance.GetQrAsync(branchId, ct));

    /// <summary>
    /// Records one scan. The direction is decided here, not sent by the phone — a
    /// browser must not be able to nominate itself as arriving all day.
    /// </summary>
    [HttpPost("scan")]
    [Authorize(Roles = Staff)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceChallengeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceChallengeDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceChallengeDto>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Scan(
        [FromBody] AttendanceScanRequest request, CancellationToken ct)
        => Envelope(await _attendance.ScanAsync(CurrentUserName ?? string.Empty, request, ct));

    /// <summary>
    /// Marks attendance from the staff member's own phone, with nothing to scan.
    /// </summary>
    /// <remarks>
    /// A phone cannot photograph a code shown on its own screen, so requiring a scan
    /// would leave anyone opening the site on their mobile with no way in. The
    /// location still decides it — the branch is the one whose fence they are
    /// standing inside — and the direction is still the server's to choose.
    /// </remarks>
    [HttpPost("mark")]
    [Authorize(Roles = Staff)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceChallengeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceChallengeDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceChallengeDto>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Mark(
        [FromBody] AttendanceMarkRequest request, CancellationToken ct)
        => Envelope(await _attendance.MarkAsync(CurrentUserName ?? string.Empty, request, ct));

    /// <summary>
    /// Confirms a mark with the six digits emailed to the staff member.
    /// </summary>
    /// <remarks>
    /// The attendance row is written here, stamped with the time the button was
    /// pressed rather than the time the code was typed — a slow email must not
    /// shorten anyone's working day.
    /// </remarks>
    [HttpPost("confirm")]
    [Authorize(Roles = Staff)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceScanResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceScanResultDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Confirm(
        [FromBody] AttendanceConfirmRequest request, CancellationToken ct)
        => Envelope(await _attendance.ConfirmAsync(CurrentUserName ?? string.Empty, request, ct));

    /// <summary>Sends a fresh code for a mark that is still waiting.</summary>
    [HttpPost("resend/{challengeId:int}")]
    [Authorize(Roles = Staff)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceChallengeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resend(int challengeId, CancellationToken ct)
        => Envelope(await _attendance.ResendAsync(CurrentUserName ?? string.Empty, challengeId, ct));

    /// <summary>The signed-in staff member's own day: their scans and hours so far.</summary>
    [HttpGet("me")]
    [Authorize(Roles = Staff)]
    [ProducesResponseType(typeof(ApiResponse<MyAttendanceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me([FromQuery] DateOnly? onDate, CancellationToken ct)
        => Envelope(await _attendance.GetMyDayAsync(CurrentUserName ?? string.Empty, onDate, ct));

    /// <summary>
    /// Attendance and hours by day, month or year. Back office only — this is every
    /// member of staff's working time, not their own record.
    /// </summary>
    [HttpGet("report")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceReportDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Report(
        [FromQuery] AttendanceReportQuery query, CancellationToken ct)
        => Envelope(await _attendance.GetReportAsync(query, ct));

    /* ---------------------------------------------------------- the rota */

    /// <summary>The shifts that exist — Morning and Evening, with their deadlines.</summary>
    [HttpGet("shifts")]
    [Authorize(Roles = $"{UserRoles.BackOffice},{Staff}")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AttendanceShiftDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Shifts(CancellationToken ct)
        => Envelope(await _attendance.GetShiftsAsync(ct));

    /// <summary>
    /// Who works which shift on which day. Back office only: this is the staff rota,
    /// and changing it changes when somebody's day is written off as leave.
    /// </summary>
    [HttpGet("rota")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StaffRotaDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Rota([FromQuery] string? staffType, CancellationToken ct)
        => Envelope(await _attendance.GetRotaAsync(staffType, ct));

    /// <summary>Replaces one person's rota with exactly what the screen is showing.</summary>
    [HttpPut("rota")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveRota(
        [FromBody] SaveRotaRequest request, CancellationToken ct)
        => Envelope(
            await _attendance.SaveRotaAsync(request, CurrentUserName, ct),
            "Rota saved.");
}
