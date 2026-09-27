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
    public async Task<IActionResult> Summary([FromQuery] DateOnly? cycleMonth, CancellationToken ct)
        => Envelope(await _dashboard.GetSummaryAsync(cycleMonth, ct));

    /// <summary>
    /// Marks one enquiry from the public contact form as dealt with, removing it
    /// from the dashboard. The message itself is kept.
    /// </summary>
    [HttpPatch("enquiries/{id:int}/handled")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkEnquiryHandled(
        int id, [FromQuery] bool handled = true, CancellationToken ct = default)
        => Envelope(
            await _dashboard.MarkEnquiryHandledAsync(id, handled, ct),
            handled ? "Enquiry marked as read." : "Enquiry moved back to new.");

    /// <summary>
    /// Enquiries from the public contact form, so a message can be found again after
    /// it has been marked read.
    /// </summary>
    /// <remarks>
    /// The dashboard panel shows only unread ones, which is what makes it a list of
    /// work rather than an archive. This is where the archive lives: pass
    /// <c>filter=Read</c> for the ones already dealt with, or <c>All</c> for
    /// everything ever submitted.
    /// </remarks>
    [HttpGet("enquiries")]
    [ProducesResponseType(typeof(ApiResponse<EnquiryListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Enquiries(
        [FromQuery] EnquiryFilter filter = EnquiryFilter.New,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
        => Envelope(await _dashboard.GetEnquiriesAsync(
            filter, page, Math.Clamp(pageSize, 1, 200), ct));

    /// <summary>Dismisses one dashboard alert.</summary>
    [HttpPatch("notifications/{id:int}/read")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkNotificationRead(int id, CancellationToken ct)
        => Envelope(await _dashboard.MarkNotificationReadAsync(id, CurrentUserName, ct), "Marked as read.");

    /// <summary>Dismisses every unread alert at once.</summary>
    [HttpPatch("notifications/read-all")]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllNotificationsRead(CancellationToken ct)
        => Envelope(await _dashboard.MarkAllNotificationsReadAsync(CurrentUserName, ct), "All marked as read.");
}
