using InstituteMS.Application.About;
using InstituteMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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

    public PublicController(IAboutService about) => _about = about;

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
