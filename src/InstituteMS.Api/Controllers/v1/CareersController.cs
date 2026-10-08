using InstituteMS.Application.Careers;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Job vacancies on the public site (Receptionist posts, per branch). Anyone
/// can read the open ones and apply; managing them and reading applications is
/// Admin's and Editor's (and SuperAdmin's, through SuperAdminAuthorizationHandler).
/// </summary>
[Route("api/v1/careers")]
[Tags("Careers")]
public class CareersController : ApiControllerBase
{
    private readonly ICareerService _careers;

    public CareersController(ICareerService careers) => _careers = careers;

    /// <summary>Vacancies open today. Public.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<VacancyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Open(CancellationToken ct)
        => Envelope(Result<IReadOnlyList<VacancyDto>>.Success(await _careers.GetOpenAsync(ct)));

    /// <summary>Applies for a vacancy with a CV and photograph. Public, rate-limited.</summary>
    [HttpPost("{id:int}/apply")]
    [AllowAnonymous]
    [EnableRateLimiting("contact")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Apply(int id, [FromBody] JobApplyRequest request, CancellationToken ct)
        => Envelope(await _careers.ApplyAsync(id, request, ct),
            "Thank you — your application has been received. The office will contact you.");

    /* ================================================================ Admin */

    [HttpGet("vacancies")]
    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.Editor)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<VacancyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> All(CancellationToken ct)
        => Envelope(Result<IReadOnlyList<VacancyDto>>.Success(await _careers.GetAllAsync(ct)));

    [HttpPost("vacancies")]
    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.Editor)]
    public async Task<IActionResult> Create([FromBody] VacancySaveRequest request, CancellationToken ct)
        => Envelope(await _careers.SaveAsync(null, request, CurrentUserName, ct), "Vacancy saved.");

    [HttpPut("vacancies/{id:int}")]
    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.Editor)]
    public async Task<IActionResult> Update(int id, [FromBody] VacancySaveRequest request, CancellationToken ct)
        => Envelope(await _careers.SaveAsync(id, request, CurrentUserName, ct), "Vacancy saved.");

    [HttpDelete("vacancies/{id:int}")]
    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.Editor)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _careers.DeleteAsync(id, ct), "Vacancy deleted.");

    [HttpGet("applications")]
    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.Editor)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<JobApplicationDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Applications([FromQuery] int? vacancyId, CancellationToken ct)
        => Envelope(Result<IReadOnlyList<JobApplicationDto>>.Success(await _careers.GetApplicationsAsync(vacancyId, ct)));

    [HttpPut("applications/{id:int}/status")]
    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.Editor)]
    public async Task<IActionResult> SetStatus(int id, [FromBody] JobApplicationStatusRequest request, CancellationToken ct)
        => Envelope(await _careers.SetStatusAsync(id, request, CurrentUserName, ct), "Application updated.");

    /// <summary>The applicant's CV (<c>kind=cv</c>) or photograph (<c>kind=photo</c>).</summary>
    [HttpGet("applications/{id:int}/file")]
    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.Editor)]
    public async Task<IActionResult> ApplicationFile(int id, [FromQuery] string kind, CancellationToken ct)
    {
        var result = await _careers.GetFileAsync(id, kind, ct);
        if (!result.Succeeded || result.Value is null) return NotFound();
        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }
}
