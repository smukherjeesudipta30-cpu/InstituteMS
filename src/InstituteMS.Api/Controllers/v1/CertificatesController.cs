using InstituteMS.Application.Certificates;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The certificate's signatory (Admin), and the public check behind the QR code
/// printed on every certificate.
/// </summary>
[Route("api/v1/certificates")]
[Tags("Certificates")]
public class CertificatesController : ApiControllerBase
{
    private readonly ICertificateService _certificates;

    public CertificatesController(ICertificateService certificates) => _certificates = certificates;

    [HttpGet("settings")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<CertificateSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Settings(CancellationToken ct)
        => Envelope(Result<CertificateSettingsDto>.Success(await _certificates.GetSettingsAsync(ct)));

    [HttpPut("settings")]
    [Authorize(Roles = UserRoles.Admin)]
    [RequestSizeLimit(4 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<CertificateSettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveSettings([FromBody] CertificateSettingsSaveRequest request, CancellationToken ct)
        => Envelope(await _certificates.SaveSettingsAsync(request, CurrentUserName, ct), "Certificate signature saved.");

    /// <summary>Confirms a certificate from its QR code. Public — an employer or college checks it here.</summary>
    [HttpGet("verify/{code}")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<CertificateVerificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CertificateVerificationDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Verify(string code, CancellationToken ct)
        => Envelope(await _certificates.VerifyAsync(code, ct));
}
