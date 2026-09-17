using InstituteMS.Application.Common;
using InstituteMS.Application.Enquiries;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The walk-in enquiry register: someone comes to the counter asking about a
/// course, and the receptionist takes their details so it can be followed up.
/// </summary>
/// <remarks>
/// Not to be confused with <c>/public/contact</c>, which is the website's
/// "Send Us Message" form and lands on the dashboard as an enquiry to answer.
/// This one is typed by a person at the desk and is chased over days or weeks.
/// </remarks>
[Route("api/v1/enquiries")]
[Tags("Enquiries")]
[Authorize(Roles = Roles)]
public class EnquiriesController : ApiControllerBase
{
    /// <summary>
    /// The receptionist takes the enquiries, so they need the register as much as
    /// the back office does — and the administrator needs the report.
    /// </summary>
    private const string Roles = $"{UserRoles.Admin},{UserRoles.Editor},{UserRoles.Receptionist}";

    private readonly IEnquiryService _enquiries;

    public EnquiriesController(IEnquiryService enquiries) => _enquiries = enquiries;

    /// <summary>
    /// The register. Due follow-ups first, then soonest due, then newest.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<CourseEnquiryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] CourseEnquiryQuery query, CancellationToken ct)
        => Envelope(await _enquiries.GetListAsync(query, ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<CourseEnquiryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
        => Envelope(await _enquiries.GetByIdAsync(id, ct));

    /// <summary>Takes a new enquiry at the counter.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Create(
        [FromBody] CourseEnquirySaveRequest request, CancellationToken ct)
        => Envelope(await _enquiries.CreateAsync(request, CurrentUserName, ct), "Enquiry saved.");

    /// <summary>
    /// Corrects the details. Does not move the status — that belongs to the
    /// follow-up, which also stamps the call and the count.
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id, [FromBody] CourseEnquirySaveRequest request, CancellationToken ct)
        => Envelope(await _enquiries.UpdateAsync(id, request, CurrentUserName, ct), "Enquiry updated.");

    /// <summary>Records a call: the new status, what was said, and the next date.</summary>
    [HttpPost("{id:int}/follow-up")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordFollowUp(
        int id, [FromBody] CourseEnquiryFollowUpRequest request, CancellationToken ct)
        => Envelope(await _enquiries.RecordFollowUpAsync(id, request, CurrentUserName, ct), "Follow-up recorded.");

    /// <summary>
    /// Removes an enquiry. Administrator only: the register is the record of who
    /// walked in, and a receptionist tidying away the ones they did not convert
    /// would quietly empty the report.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _enquiries.DeleteAsync(id, ct), "Enquiry deleted.");

    /// <summary>
    /// The follow-up report: the filtered rows plus the totals — how many came in,
    /// how many converted, and how many are overdue a call.
    /// </summary>
    [HttpGet("report")]
    [ProducesResponseType(typeof(ApiResponse<CourseEnquiryReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Report([FromQuery] CourseEnquiryQuery query, CancellationToken ct)
        => Envelope(await _enquiries.GetReportAsync(query, ct));
}
