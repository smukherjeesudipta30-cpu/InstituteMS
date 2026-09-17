using InstituteMS.Application.About;
using InstituteMS.Application.Common;
using InstituteMS.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Anonymous endpoints backing the public About Us page — replaces
/// <c>Views/About/Index.cshtml</c> and the <c>CusMessage</c> insert behind it.
/// </summary>
[Route("api/v1/public")]
[Tags("Public")]
[AllowAnonymous]
public class PublicController : ApiControllerBase
{
    private readonly IAboutService _about;
    private readonly AttendanceOptions _attendance;
    private readonly OrgOptions _org;
    private readonly CompetitionOptions _competition;

    public PublicController(
        IAboutService about,
        IOptions<AttendanceOptions> attendance,
        IOptions<OrgOptions> org,
        IOptions<CompetitionOptions> competition)
    {
        _about = about;
        _attendance = attendance.Value;
        _org = org.Value;
        _competition = competition.Value;
    }

    /// <summary>
    /// Which optional features are switched on, so the front end knows what to show
    /// in its menu before anyone has signed in.
    /// </summary>
    /// <remarks>
    /// Anonymous deliberately: it is read while the sign-in page is still loading,
    /// and it reveals nothing beyond the existence of a feature. Everything behind
    /// each feature is still guarded by role on its own endpoints.
    /// </remarks>
    [HttpGet("features")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public IActionResult Features()
        => Ok(ApiResponse<object>.Ok(new
        {
            attendance = _attendance.Enabled,
            org = _org.Enabled,
            competition = _competition.Enabled
        }));

    /// <summary>About Us copy, live headline stats, the course list and contact details.</summary>
    [HttpGet("about")]
    [ProducesResponseType(typeof(ApiResponse<AboutPageDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> About(CancellationToken ct)
        => Envelope(await _about.GetAboutPageAsync(ct));

    /// <summary>Submits a contact-us enquiry. Rate limited — this endpoint is open to the internet.</summary>
    [HttpPost("contact")]
    [EnableRateLimiting("contact")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Contact([FromBody] ContactRequest request, CancellationToken ct)
        => Envelope(
            await _about.SubmitContactAsync(request, ClientIp, ct),
            "Thank you — your message has been received.");
}
