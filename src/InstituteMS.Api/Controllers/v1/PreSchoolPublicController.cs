using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The public PreSchool page: open to visitors who have not signed in. The
/// enquiry form is rate-limited like the Computer Centre's contact form.
/// </summary>
[Route("api/v1/preschool/public")]
[Tags("PreSchool")]
[AllowAnonymous]
public class PreSchoolPublicController : ApiControllerBase
{
    private readonly IPreSchoolPublicService _public;

    public PreSchoolPublicController(IPreSchoolPublicService service) => _public = service;

    /// <summary>The school's contact details, uniform shop and active classes with their fees.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolPublicDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Envelope(Result<PreSchoolPublicDto>.Success(await _public.GetPublicAsync(ct)));

    /// <summary>An admission enquiry from a parent, for the PreSchool office to call back.</summary>
    [HttpPost("enquiries")]
    [EnableRateLimiting("contact")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Enquire([FromBody] PreSchoolEnquiryRequest request, CancellationToken ct)
        => Envelope(await _public.SubmitEnquiryAsync(request, ClientIp, ct),
            "Thank you. The PreSchool will call you soon.");
}
