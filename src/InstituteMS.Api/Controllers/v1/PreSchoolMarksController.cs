using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// PreSchool exam marks. The admin, receptionist and teachers enter them; the
/// admin and receptionist keep the list of exams. No Computer Centre role.
/// </summary>
[Route("api/v1/preschool")]
[Tags("PreSchool")]
[Authorize(Roles = UserRoles.PreSchoolStaff)]
public class PreSchoolMarksController : ApiControllerBase
{
    private readonly IPreSchoolMarkService _marks;

    public PreSchoolMarksController(IPreSchoolMarkService marks) => _marks = marks;

    [HttpGet("exams")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PreSchoolExamDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Exams(CancellationToken ct)
        => Envelope(Result<IReadOnlyList<PreSchoolExamDto>>.Success(await _marks.ExamsAsync(ct)));

    [HttpPost("exams")]
    [Authorize(Roles = UserRoles.PreSchoolOffice)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolExamDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddExam([FromBody] PreSchoolExamSaveRequest request, CancellationToken ct)
        => Envelope(await _marks.SaveExamAsync(null, request, CurrentUserName, ct), "Exam added.");

    [HttpPut("exams/{id:int}")]
    [Authorize(Roles = UserRoles.PreSchoolOffice)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolExamDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveExam(int id, [FromBody] PreSchoolExamSaveRequest request, CancellationToken ct)
        => Envelope(await _marks.SaveExamAsync(id, request, CurrentUserName, ct), "Exam saved.");

    /// <summary>The marks sheet for one exam and one class.</summary>
    [HttpGet("marks")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolMarkSheetDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sheet([FromQuery] int examId, [FromQuery] int classId, CancellationToken ct)
        => Envelope(await _marks.SheetAsync(examId, classId, ct));

    /// <summary>Saves the marks typed on a sheet; an empty cell clears that mark.</summary>
    [HttpPut("marks")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolMarkSheetDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Save([FromBody] PreSchoolMarkSaveRequest request, CancellationToken ct)
        => Envelope(await _marks.SaveAsync(request, CurrentUserName, ct), "Marks saved.");
}
