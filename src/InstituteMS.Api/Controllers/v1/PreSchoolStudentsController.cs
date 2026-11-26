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
    private readonly IPreSchoolPhotoService _photos;

    public PreSchoolStudentsController(IPreSchoolStudentService students, IPreSchoolPhotoService photos)
    {
        _students = students;
        _photos = photos;
    }

    /// <summary>The child's and both guardians' photos, for the form and the ID cards.</summary>
    [HttpGet("students/{id:int}/photos")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolStudentPhotosDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Photos(int id, CancellationToken ct)
        => Envelope(await _photos.GetStudentPhotosAsync(id, ct));

    /// <summary>Saves one photo (Child, Guardian1 or Guardian2); an empty one removes it.</summary>
    [HttpPut("students/{id:int}/photos/{slot}")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolStudentPhotosDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SavePhoto(int id, string slot, [FromBody] PreSchoolPhotoSaveRequest request, CancellationToken ct)
        => Envelope(await _photos.SaveStudentPhotoAsync(id, slot, request, CurrentUserName, ct), "Photo saved.");

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

    /// <summary>
    /// Emails the receipt to the child's guardian from the PreSchool's mailbox,
    /// with the parent copy attached when the screen sends it as a PDF.
    /// </summary>
    [HttpPost("receipts/{id:int}/email")]
    [RequestSizeLimit(4_000_000)]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolReceiptMailResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> EmailReceipt(int id, [FromBody] PreSchoolReceiptEmailRequest request, CancellationToken ct)
    {
        byte[]? pdf = null;
        if (!string.IsNullOrWhiteSpace(request.PdfBase64))
        {
            var b64 = request.PdfBase64;
            var comma = b64.IndexOf(',');
            if (b64.StartsWith("data:") && comma > 0) b64 = b64[(comma + 1)..];
            try { pdf = Convert.FromBase64String(b64); } catch (FormatException) { pdf = null; }
            // Only a real PDF is attached.
            if (pdf is not { Length: > 4 } || pdf[0] != '%' || pdf[1] != 'P' || pdf[2] != 'D' || pdf[3] != 'F') pdf = null;
        }
        return Envelope(await _students.EmailReceiptAsync(id, pdf, CurrentUserName, ct));
    }

    /// <summary>Deletes a receipt entered by mistake (PreSchool admin or receptionist); the admin is alerted.</summary>
    [HttpDelete("receipts/{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteReceipt(int id, CancellationToken ct)
        => Envelope(await _students.DeleteReceiptAsync(id, CurrentUserName, ActorRole, ct), "Receipt deleted.");

    /// <summary>Deletes the latest instalment of a receipt, entered by mistake; the admin is alerted.</summary>
    [HttpDelete("receipts/{id:int}/payments/{paymentId:int}")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolReceiptDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeletePayment(int id, int paymentId, CancellationToken ct)
        => Envelope(await _students.DeletePaymentAsync(id, paymentId, CurrentUserName, ActorRole, ct), "Instalment deleted.");

    /// <summary>Recorded on the alert, so the admin's own deletions do not alert the admin.</summary>
    private string ActorRole =>
        User.IsInRole(UserRoles.SuperAdmin) ? UserRoles.SuperAdmin
        : User.IsInRole(UserRoles.PreSchoolHead) ? UserRoles.PreSchoolHead
        : UserRoles.PreSchoolFrontDesk;

    /// <summary>The PreSchool's teachers and receptionists who can be named as a child's referrer.</summary>
    [HttpGet("referrers")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PreSchoolReferrerDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Referrers(CancellationToken ct)
        => Envelope(Result<IReadOnlyList<PreSchoolReferrerDto>>.Success(await _students.ReferrersAsync(ct)));

    /// <summary>Every payment received between two dates, with who collected it and by what mode.</summary>
    [HttpGet("reports/collections")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolCollectionReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Collections([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromServices] IPreSchoolFinanceService finance, CancellationToken ct)
        => Envelope(await finance.CollectionReportAsync(from, to, ct));

    /// <summary>A further instalment against a receipt being paid in parts; the login that took it is recorded.</summary>
    [HttpPost("receipts/{id:int}/payments")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolReceiptDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddPayment(int id, [FromBody] PreSchoolPaymentRequest request, CancellationToken ct)
        => Envelope(await _students.AddPaymentAsync(id, request, CurrentUserName, ct), "Instalment received.");

    /// <summary>
    /// Admissions between two dates (by joining date), month by month. With no
    /// dates, one calendar year (this year when none is given).
    /// </summary>
    [HttpGet("reports/admissions")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolAdmissionReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Admissions([FromQuery] int? year, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int? classId, CancellationToken ct)
    {
        var y = year ?? DateTime.Today.Year;
        return Envelope(await _students.AdmissionsAsync(from ?? new DateOnly(y, 1, 1), to ?? new DateOnly(y, 12, 31), classId, ct));
    }

    /// <summary>Active children with unpaid months, owing at least <paramref name="minMonths"/> of them.</summary>
    [HttpGet("reports/pending-fees")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolPendingReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Pending([FromQuery] int? classId, [FromQuery] int minMonths, CancellationToken ct)
        => Envelope(Result<PreSchoolPendingReportDto>.Success(await _students.PendingAsync(classId, minMonths, ct)));
}
