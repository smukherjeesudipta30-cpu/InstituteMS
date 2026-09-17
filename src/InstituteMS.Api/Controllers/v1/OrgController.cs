using InstituteMS.Application.Common;
using InstituteMS.Application.Org;
using InstituteMS.Application.Reports;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The staff hierarchy chart — who reports to whom, shown on every staff profile,
/// every student profile and the administrator's dashboard.
/// </summary>
/// <remarks>
/// <para>Reading it is open to everybody signed in, because that is the point of it:
/// a student should be able to see who runs the centre and who teaches them. Building
/// it belongs to the back office.</para>
///
/// <para>The chart is not a fixed set of boxes. Teachers and receptionists are drawn
/// from the staff register every time it is fetched, so somebody hired next month
/// appears without anybody editing the hierarchy.</para>
/// </remarks>
[Route("api/v1/org")]
[Tags("Organisation")]
[Authorize]
public class OrgController : ApiControllerBase
{
    private readonly IOrgService _org;

    public OrgController(IOrgService org) => _org = org;

    /// <summary>
    /// The whole chart.
    /// </summary>
    /// <remarks>
    /// Email addresses come back only for the back office. The owner asked that the
    /// chart appear on student profiles and said plainly there is "no need to show
    /// email for student profile", so the addresses are dropped before the response
    /// is written rather than hidden in the page — a student who opens the network
    /// tab finds nothing to read.
    /// </remarks>
    [HttpGet("chart")]
    [ProducesResponseType(typeof(ApiResponse<OrgChartDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Chart(CancellationToken ct)
        => Envelope(await _org.GetChartAsync(IsBackOffice, ct));

    /// <summary>
    /// The signed-in student's own line of the hierarchy: the owner, and beneath
    /// them the teacher or teachers they are actually assigned to.
    /// </summary>
    /// <remarks>
    /// <para>Resolved from their own sign-in rather than from anything the browser
    /// sends, so a student can only ever be shown their own teachers.</para>
    ///
    /// <para>It follows the student record, so it is right as soon as the admission
    /// is taken and it updates itself when the office changes their teacher.</para>
    /// </remarks>
    [HttpGet("my-teachers")]
    [Authorize(Roles = UserRoles.Student)]
    [ProducesResponseType(typeof(ApiResponse<OrgChartDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<OrgChartDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MyTeachers(
        [FromServices] IReportService reports, CancellationToken ct)
    {
        var own = await reports.ResolveOwnRegistrationIdAsync(CurrentUserName ?? string.Empty, ct);

        if (!own.Succeeded)
            return Envelope(Result<OrgChartDto>.Failure(own.Error!, own.Status));

        return Envelope(await _org.GetStudentChartAsync(own.Value!, ct));
    }

    /// <summary>Adds a box.</summary>
    [HttpPost("nodes")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] OrgNodeSaveRequest request, CancellationToken ct)
        => Envelope(await _org.CreateAsync(request, CurrentUserName, ct), "Added to the chart.");

    /// <summary>Edits a box — name, position, qualification, branch, active, parents.</summary>
    [HttpPut("nodes/{id:int}")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id, [FromBody] OrgNodeSaveRequest request, CancellationToken ct)
        => Envelope(await _org.UpdateAsync(id, request, CurrentUserName, ct), "Chart updated.");

    /// <summary>
    /// Removes a box. Refused while anybody still reports to it, so nobody drops off
    /// the chart as a side effect.
    /// </summary>
    [HttpDelete("nodes/{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _org.DeleteAsync(id, ct), "Removed from the chart.");

    /// <summary>The photograph on one box.</summary>
    [HttpGet("nodes/{id:int}/photo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Photo(int id, CancellationToken ct)
    {
        var result = await _org.GetPhotoAsync(id, ct);

        if (!result.Succeeded || result.Value is null)
            return NotFound();

        /* The chart shows several photographs at once and is opened repeatedly — the
           dashboard, a profile, the team page — so without this every visit re-fetches
           every picture. Private, because these are staff photographs and have no
           business in a shared proxy's cache. The tag is over the bytes themselves, so
           replacing a photograph invalidates it immediately. */
        var etag = $"\"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(result.Value.Content).AsSpan(0, 8))}\"";

        if (Request.Headers.IfNoneMatch.Contains(etag))
            return StatusCode(StatusCodes.Status304NotModified);

        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers.ETag = etag;

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    /// <summary>Uploads or replaces the photograph on one box.</summary>
    [HttpPost("nodes/{id:int}/photo")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadPhoto(
        int id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Envelope(Result<bool>.Failure("Choose a photograph to upload."));

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);

        return Envelope(
            await _org.SetPhotoAsync(
                id, stream.ToArray(), file.FileName, file.ContentType, CurrentUserName, ct),
            "Photograph saved.");
    }
}
