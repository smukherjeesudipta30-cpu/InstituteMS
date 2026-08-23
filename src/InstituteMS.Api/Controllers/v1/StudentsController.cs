using InstituteMS.Application.Common;
using InstituteMS.Application.Students;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Student registration and roster. Replaces <c>HomeController</c> +
/// <c>Views/Home/Index.cshtml</c> + <c>Scripts/Student.js</c> and implements
/// MIGRATION_PLAN §5.2.
/// </summary>
[Route("api/v1/students")]
[Tags("Students")]
[Authorize(Roles = UserRoles.BackOffice)]
public class StudentsController : ApiControllerBase
{
    private readonly IStudentService _students;

    public StudentsController(IStudentService students) => _students = students;

    /// <summary>
    /// Paged roster. Replaces <c>getDetails</c>, <c>getInactiveDetails</c>,
    /// <c>SearchData</c> and <c>SearchData1</c> — one query with filters instead of
    /// four near-identical ones (defect D9).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<StudentListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] StudentQuery query, CancellationToken ct)
        => Envelope(await _students.GetListAsync(query, ct));

    /// <summary>A single student by primary key.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<StudentListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Envelope(await _students.GetByIdAsync(id, ct));

    /// <summary>
    /// A single student by registration id. Where the legacy data has duplicates on
    /// that id, the most recent record wins.
    /// </summary>
    /// <remarks>
    /// The id is a query parameter, not a route segment: registration ids contain
    /// forward slashes (<c>AVI/3451/26</c>) and Kestrel decodes <c>%2F</c> before
    /// routing, which would split the value across segments and never match.
    /// </remarks>
    [HttpGet("by-reg")]
    [ProducesResponseType(typeof(ApiResponse<StudentListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByRegistrationId(
        [FromQuery] string registrationId, CancellationToken ct)
        => Envelope(await _students.GetByRegistrationIdAsync(registrationId, ct));

    /// <summary>Name/registration search for the re-registration picker (legacy <c>SearchDatRegistration</c>).</summary>
    [HttpGet("lookup")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<StudentLookupDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Lookup([FromQuery] string? search, CancellationToken ct)
        => Envelope(await _students.GetLookupAsync(search, ct));

    /// <summary>
    /// Preview of the registration and bill numbers the next admission will get.
    /// Advisory only — the real pair is reserved under lock when the record is saved.
    /// </summary>
    /// <remarks>
    /// With a <c>branchId</c> the preview reads <c>AVI/Raj/3463/26</c>; without one it
    /// falls back to the older <c>AVI/3463/26</c>.
    /// </remarks>
    [HttpGet("next-registration-no")]
    [ProducesResponseType(typeof(ApiResponse<NextRegistrationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> NextRegistrationNo([FromQuery] int? branchId, CancellationToken ct)
        => Envelope(await _students.PeekNextRegistrationAsync(branchId, ct));

    /// <summary>Admission receipt data — replaces <c>getPrint</c> / <c>PrintReport.cshtml</c>.</summary>
    [HttpGet("{id:int}/receipt")]
    [ProducesResponseType(typeof(ApiResponse<AdmissionReceiptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Receipt(int id, CancellationToken ct)
        => Envelope(await _students.GetReceiptAsync(id, ct));

    /// <summary>Student photo, decoded from the Base64 blob the legacy uploader stored.</summary>
    [HttpGet("photo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Photo([FromQuery] string registrationId, CancellationToken ct)
    {
        var result = await _students.GetPhotoAsync(registrationId, ct);

        if (!result.Succeeded)
            return Envelope(Result<object?>.Failure(result.Error!, result.Status));

        var (content, contentType, fileName) = result.Value;
        Response.Headers.CacheControl = "private, max-age=3600";

        return File(content, contentType, fileName);
    }

    /// <summary>
    /// Uploads a student photo. Replaces <c>Views/ImageStudent/Index.cshtml</c> +
    /// <c>Scripts/ImageFile.js</c> + <c>Home/SaveFiles</c>.
    /// </summary>
    /// <remarks>
    /// Same storage as the legacy app — Base64 in <c>ImageUpload</c> — but PNG/JPEG/GIF
    /// only, 512 KB maximum, and the bytes are checked against their magic numbers so
    /// a renamed file cannot get through (defect D10). Replaces any existing photo.
    /// </remarks>
    [HttpPost("photo")]
    [RequestSizeLimit(1 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<StudentPhotoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StudentPhotoDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadPhoto(
        [FromQuery] string registrationId,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Envelope(Result<StudentPhotoDto>.Failure("Please choose an image to upload.", ResultStatus.BadRequest));

        await using var content = file.OpenReadStream();

        var upload = new PhotoUpload(file.FileName, file.ContentType, file.Length, content);

        return Envelope(
            await _students.UploadPhotoAsync(registrationId, upload, CurrentUserName, ct),
            "Photo uploaded.");
    }

    /// <summary>Removes the student's photo.</summary>
    [HttpDelete("photo")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePhoto([FromQuery] string registrationId, CancellationToken ct)
        => Envelope(await _students.DeletePhotoAsync(registrationId, ct), "Photo removed.");

    /// <summary>
    /// Admits a student. The registration id and bill number are generated server-side
    /// inside a transaction — the client cannot supply them.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<StudentCreatedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StudentCreatedDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] StudentSaveRequest request, CancellationToken ct)
        => Envelope(await _students.CreateAsync(request, CurrentUserName, ct), "Student saved successfully.");

    /// <summary>Updates a student. Registration id and bill number are immutable.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] StudentSaveRequest request, CancellationToken ct)
        => Envelope(await _students.UpdateAsync(id, request, CurrentUserName, ct), "Student updated successfully.");

    /// <summary>
    /// Soft delete — sets IsActive = 0 and stamps the exit date. Admin only, because
    /// the legacy equivalent destroyed the row outright.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
        => Envelope(await _students.DeactivateAsync(id, CurrentUserName, ct), "Student deactivated.");

    /// <summary>Reactivates a student by registration id (legacy <c>ActivateStudent</c>).</summary>
    [HttpPatch("activate")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate([FromQuery] string registrationId, CancellationToken ct)
        => Envelope(await _students.ActivateAsync(registrationId, CurrentUserName, ct), "Student reactivated.");
}
