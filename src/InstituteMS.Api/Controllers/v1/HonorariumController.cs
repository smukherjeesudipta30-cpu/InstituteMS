using InstituteMS.Application.Common;
using InstituteMS.Application.Income;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The proprietor's monthly honorarium: seen by Admin, Editor and SuperAdmin,
/// set by Admin. Deducted from the whole institute's profit, never a branch's.
/// </summary>
[Route("api/v1/proprietor-honorarium")]
[Tags("Income")]
[Authorize(Roles = UserRoles.BackOffice)]
public class HonorariumController : ApiControllerBase
{
    private readonly IHonorariumService _honorarium;

    public HonorariumController(IHonorariumService honorarium) => _honorarium = honorarium;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<HonorariumDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Envelope(Result<HonorariumDto>.Success(await _honorarium.GetAsync(ct)));

    /// <summary>Sets the monthly amount from a month onwards.</summary>
    [HttpPut]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<HonorariumDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Save([FromBody] HonorariumSaveRequest request, CancellationToken ct)
        => Envelope(await _honorarium.SaveAsync(request, CurrentUserName, ct), "Honorarium saved.");

    [HttpDelete("{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<HonorariumDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _honorarium.DeleteAsync(id, ct), "Honorarium entry removed.");
}
