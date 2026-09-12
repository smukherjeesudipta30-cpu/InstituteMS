using InstituteMS.Application.Common;
using InstituteMS.Application.Masters;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Receptionist master and their monthly pay.
/// </summary>
/// <remarks>
/// Receptionists previously existed only as sign-ins, with no record of who they
/// are, which branch they work at, or what they have been paid. A receptionist is
/// paid monthly rather than per fee, so a payment names the month it covers and
/// there is one per person per month.
/// </remarks>
[Route("api/v1/receptionists")]
[Tags("Receptionists")]
[Authorize(Roles = UserRoles.MasterData)]
public class ReceptionistsController : ApiControllerBase
{
    private readonly IReceptionistService _receptionists;

    public ReceptionistsController(IReceptionistService receptionists)
        => _receptionists = receptionists;

    /// <summary>Paged list with each person's months paid and total.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ReceptionistListItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ReceptionistQuery query, CancellationToken ct)
        => Envelope(await _receptionists.GetListAsync(query, ct));

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] ReceptionistSaveRequest request, CancellationToken ct)
        => Envelope(await _receptionists.CreateAsync(request, CurrentUserName, ct), "Receptionist saved.");

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id, [FromBody] ReceptionistSaveRequest request, CancellationToken ct)
        => Envelope(await _receptionists.UpdateAsync(id, request, CurrentUserName, ct), "Receptionist updated.");

    /// <summary>Removes a receptionist. Refused once they have been paid.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _receptionists.DeleteAsync(id, ct), "Receptionist deleted.");

    /* ------------------------------------------------------ payments */

    // One person's salary history. Back office only — the class rule is MasterData
    // so the front desk can keep the receptionist records up to date, but that must
    // not hand one receptionist their colleagues' pay.
    [HttpGet("{id:int}/payments")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ReceptionistPaymentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Payments(
        int id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        => Envelope(await _receptionists.GetPaymentsAsync(id, from, to, ct));

    /// <summary>
    /// Records one month's pay, or corrects the month already recorded.
    /// </summary>
    /// <remarks>
    /// <b>Administrator only</b>, like paying a teacher: it moves money out of the
    /// institute.
    /// </remarks>
    [HttpPost("{id:int}/payments")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecordPayment(
        int id, [FromBody] ReceptionistPaymentRequest request, CancellationToken ct)
        => Envelope(
            await _receptionists.RecordPaymentAsync(request with { ReceptionistId = id }, CurrentUserName, ct),
            "Payment recorded.");

    [HttpDelete("payments/{paymentId:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeletePayment(int paymentId, CancellationToken ct)
        => Envelope(await _receptionists.DeletePaymentAsync(paymentId, ct), "Payment removed.");

    /// <summary>Receptionist pay by branch and month.</summary>
    // Everyone's salary on one page. Back office only, for the reason above.
    [HttpGet("report")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<ReceptionistReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ReceptionistReportDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Report(
        [FromQuery] ReceptionistReportQuery query, CancellationToken ct)
        => Envelope(await _receptionists.GetReportAsync(query, ct));
}
