using InstituteMS.Application.Common;
using InstituteMS.Application.Offers;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The admission offer on the public front page. Admin and Receptionist set the
/// percentage and how many days it shows; the front page reads it anonymously.
/// </summary>
[Route("api/v1/offer")]
[Tags("Offer")]
public class OfferController : ApiControllerBase
{
    private const string Setters = $"{UserRoles.Admin},{UserRoles.Receptionist}";

    private readonly IOfferService _offers;

    public OfferController(IOfferService offers) => _offers = offers;

    /// <summary>The offer if it is showing today; null otherwise. Public — the front page asks.</summary>
    [HttpGet("public")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<OfferPublicDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Public(CancellationToken ct)
        => Envelope(await _offers.GetPublicAsync(ct));

    [HttpGet("settings")]
    [Authorize(Roles = Setters)]
    [ProducesResponseType(typeof(ApiResponse<OfferSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Settings(CancellationToken ct)
        => Envelope(await _offers.GetSettingsAsync(ct));

    [HttpPut("settings")]
    [Authorize(Roles = Setters)]
    [ProducesResponseType(typeof(ApiResponse<OfferSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveSettings([FromBody] OfferSaveRequest request, CancellationToken ct)
        => Envelope(await _offers.SaveSettingsAsync(request, CurrentUserName, ct), "Offer saved.");
}
