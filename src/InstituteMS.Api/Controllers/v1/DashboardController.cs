using InstituteMS.Application.Common;
using InstituteMS.Application.Dashboard;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Home Dashboard data. Restricted to back-office roles, mirroring
/// <c>[Authorize(Roles="Admin,Editor")]</c> on the legacy HomeController.
/// </summary>
[Route("api/v1/dashboard")]
[Tags("Dashboard")]
[Authorize(Roles = UserRoles.BackOffice)]
public class DashboardController : ApiControllerBase
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard) => _dashboard = dashboard;

    /// <summary>Counts, this-month finance, a 12-month trend, enrolment split, notices and recent admissions.</summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ApiResponse<DashboardSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Summary(CancellationToken ct)
        => Envelope(await _dashboard.GetSummaryAsync(ct));
}
