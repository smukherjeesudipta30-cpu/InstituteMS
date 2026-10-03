using InstituteMS.Application.Common;
using InstituteMS.Application.Holidays;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The holiday list: read by every signed-in role, changed by Admin (and
/// SuperAdmin, through SuperAdminAuthorizationHandler).
/// </summary>
[Route("api/v1/holidays")]
[Tags("Holidays")]
[Authorize]
public class HolidaysController : ApiControllerBase
{
    private readonly IHolidayService _holidays;

    public HolidaysController(IHolidayService holidays) => _holidays = holidays;

    /// <summary>
    /// Every holiday in one year (this year when none is given), in date order.
    /// A year opened for the first time is fetched from the Indian holiday calendar.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<HolidayYearDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get([FromQuery] int? year, CancellationToken ct)
        => Envelope(Result<HolidayYearDto>.Success(
            await _holidays.GetYearAsync(year ?? DateTime.Today.Year, ct)));

    /// <summary>
    /// Days the institute is closed this month, for the public home
    /// page — open to visitors who have not signed in.
    /// </summary>
    [HttpGet("this-month")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UpcomingClosureDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ThisMonth(CancellationToken ct)
        => Envelope(Result<IReadOnlyList<UpcomingClosureDto>>.Success(
            await _holidays.GetMonthClosedAsync(ct)));

    /// <summary>Adds any holiday the Indian calendar has for that year that is not on the list yet.</summary>
    [HttpPost("sync")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Sync([FromQuery] int year, CancellationToken ct)
        => Envelope(await _holidays.SyncFromCalendarAsync(year, CurrentUserName, ct), "Holidays fetched.");

    [HttpPost]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<HolidayDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<HolidayDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] HolidaySaveRequest request, CancellationToken ct)
        => Envelope(await _holidays.CreateAsync(request, CurrentUserName, ct), "Holiday added.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<HolidayDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<HolidayDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] HolidaySaveRequest request, CancellationToken ct)
        => Envelope(await _holidays.UpdateAsync(id, request, CurrentUserName, ct), "Holiday saved.");

    /// <summary>Starts <paramref name="toYear"/> from <paramref name="fromYear"/>'s list, same day and month.</summary>
    [HttpPost("copy")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Copy([FromQuery] int fromYear, [FromQuery] int toYear, CancellationToken ct)
        => Envelope(await _holidays.CopyYearAsync(fromYear, toYear, CurrentUserName, ct), "Holidays copied.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _holidays.DeleteAsync(id, ct), "Holiday removed.");
}
