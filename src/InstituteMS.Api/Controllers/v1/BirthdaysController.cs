using InstituteMS.Application.Birthdays;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>Upcoming birthdays of students and staff, for Admin to send wishes.</summary>
[Route("api/v1/birthdays")]
[Tags("Birthdays")]
[Authorize(Roles = UserRoles.Admin)]
public class BirthdaysController : ApiControllerBase
{
    private readonly IBirthdayService _birthdays;

    public BirthdaysController(IBirthdayService birthdays) => _birthdays = birthdays;

    /// <summary>Today's birthdays and those in the next <paramref name="days"/> days (default 30).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BirthdayPersonDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Upcoming([FromQuery] int days = 30, CancellationToken ct = default)
        => Envelope(Result<IReadOnlyList<BirthdayPersonDto>>.Success(
            await _birthdays.GetUpcomingAsync(days is >= 0 and <= 365 ? days : 30, ct)));
}
