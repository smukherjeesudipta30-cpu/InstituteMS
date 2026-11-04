using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// PreSchool admissions, fee collection, receipts and the pending-fees report.
/// The PreSchool office only (its admin and receptionist; SuperAdmin through
/// SuperAdminAuthorizationHandler). Only the PreSchool admin cancels a receipt.
/// </summary>
[Route("api/v1/preschool")]
[Tags("PreSchool")]
[Authorize(Roles = UserRoles.PreSchoolOffice)]
public class PreSchoolStudentsController : ApiControllerBase
{
    private readonly IPreSchoolStudentService _students;

    public PreSchoolStudentsController(IPreSchoolStudentService students) => _students = students;

    [HttpGet("students")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PreSchoolStudentListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] int? classId, [FromQuery] string? status, CancellationToken ct)
        => Envelope(Result<IReadOnlyList<PreSchoolStudentListItemDto>>.Success(await _students.ListAsync(search, classId, status, ct)));

    [HttpGet("students/{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolStudentDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
        => Envelope(await _students.GetAsync(id, ct));

    /// <summary>A new admission. The admission fee is collected separately, on the receipt.</summary>
    [HttpPost("students")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolStudentDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Admit([FromBody] PreSchoolStudentSaveRequest request, CancellationToken ct)
        => Envelope(await _students.AdmitAsync(request, CurrentUserName, ct), "Admission saved.");

    [HttpPut("students/{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolStudentDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, [FromBody] PreSchoolStudentSaveRequest request, CancellationToken ct)
        => Envelope(await _students.UpdateAsync(id, request, CurrentUserName, ct), "Details saved.");

    /// <summary>Collects the admission fee and/or months, and returns the receipt to print.</summary>
    [HttpPost("students/{id:int}/receipts")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolReceiptDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Collect(int id, [FromBody] PreSchoolCollectRequest request, CancellationToken ct)
        => Envelope(await _students.CollectAsync(id, request, CurrentUserName, ct), "Payment received.");

    [HttpGet("receipts/{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolReceiptDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Receipt(int id, CancellationToken ct)
        => Envelope(await _students.GetReceiptAsync(id, ct));

    [HttpPut("receipts/{id:int}/cancel")]
    [Authorize(Roles = UserRoles.PreSchoolHead)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolReceiptDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CancelReceipt(int id, CancellationToken ct)
        => Envelope(await _students.CancelReceiptAsync(id, CurrentUserName, ct), "Receipt cancelled.");

    /// <summary>Admissions in one calendar year (this year when none is given), month by month.</summary>
    [HttpGet("reports/admissions")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolAdmissionReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Admissions([FromQuery] int? year, [FromQuery] int? classId, CancellationToken ct)
        => Envelope(Result<PreSchoolAdmissionReportDto>.Success(await _students.AdmissionsAsync(year ?? DateTime.Today.Year, classId, ct)));

    /// <summary>Active children with unpaid months, owing at least <paramref name="minMonths"/> of them.</summary>
    [HttpGet("reports/pending-fees")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolPendingReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Pending([FromQuery] int? classId, [FromQuery] int minMonths, CancellationToken ct)
        => Envelope(Result<PreSchoolPendingReportDto>.Success(await _students.PendingAsync(classId, minMonths, ct)));
}
