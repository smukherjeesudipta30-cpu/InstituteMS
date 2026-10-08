using InstituteMS.Application.Analytics;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Visitors to the public pages: recorded anonymously by the page itself, and
/// summarised for Admin and Editor (and SuperAdmin) to see how promotion works.
/// </summary>
[Route("api/v1/site-visits")]
[Tags("Site visits")]
public class SiteVisitsController : ApiControllerBase
{
    private readonly ISiteVisitService _visits;

    public SiteVisitsController(ISiteVisitService visits) => _visits = visits;

    /// <summary>Records a visit to a public page. Public; never fails the page.</summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Record([FromBody] SiteVisitRequest request, CancellationToken ct)
    {
        try
        {
            await _visits.RecordAsync(request, ClientIp, Request.Headers.UserAgent.ToString(), ct);
        }
        catch (Exception)
        {
            // A visit counter must never be the reason a page misbehaves.
        }
        return NoContent();
    }

    /// <summary>Visitors over the last <paramref name="days"/> days (30 by default).</summary>
    [HttpGet("summary")]
    [Authorize(Roles = UserRoles.Admin + "," + UserRoles.Editor)]
    [ProducesResponseType(typeof(ApiResponse<SiteVisitSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary([FromQuery] int days, CancellationToken ct)
        => Envelope(Result<SiteVisitSummaryDto>.Success(await _visits.GetSummaryAsync(days <= 0 ? 30 : days, ct)));
}
