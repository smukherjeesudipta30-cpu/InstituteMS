using InstituteMS.Application.Common;
using InstituteMS.Application.Referrals;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Admission referral commission: who can be named as a referrer, the slabs
/// deciding the percentage, and the credits earned.
/// </summary>
[Route("api/v1/referrals")]
[Tags("Referrals")]
[Authorize(Roles = UserRoles.MasterData)]
public class ReferralsController : ApiControllerBase
{
    private readonly IReferralService _referrals;

    public ReferralsController(IReferralService referrals) => _referrals = referrals;

    /// <summary>Active teachers and receptionists, for the admission form's Reference dropdown.</summary>
    [HttpGet("referrers")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ReferrerOptionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Referrers(CancellationToken ct)
        => Envelope(Result<IReadOnlyList<ReferrerOptionDto>>.Success(await _referrals.GetReferrersAsync(ct)));

    /// <summary>The slabs. Readable by anyone admitting students, so the form can show the rate.</summary>
    [HttpGet("settings")]
    [ProducesResponseType(typeof(ApiResponse<ReferralSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Settings(CancellationToken ct)
        => Envelope(Result<ReferralSettingsDto>.Success(await _referrals.GetSettingsAsync(ct)));

    /// <summary>Changes the slabs. Admin only; applies to students admitted from now on.</summary>
    [HttpPut("settings")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<ReferralSettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ReferralSettingsDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveSettings([FromBody] ReferralSettingsSaveRequest request, CancellationToken ct)
        => Envelope(await _referrals.SaveSettingsAsync(request, CurrentUserName, ct), "Referral slabs saved.");

    /// <summary>Referral credits, optionally for one referrer and a date range.</summary>
    [HttpGet("credits")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ReferralCreditDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Credits(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] string? referrerType, [FromQuery] int? referrerId, CancellationToken ct)
        => Envelope(Result<IReadOnlyList<ReferralCreditDto>>.Success(
            await _referrals.GetCreditsAsync(from, to, referrerType, referrerId, ct)));
}
