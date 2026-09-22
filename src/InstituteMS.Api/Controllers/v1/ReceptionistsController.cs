using InstituteMS.Application.Common;
using InstituteMS.Application.Masters;
using InstituteMS.Application.Reports;
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
    private readonly IReportService _reports;

    public ReceptionistsController(IReceptionistService receptionists, IReportService reports)
    {
        _receptionists = receptionists;
        _reports = reports;
    }

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

    /// <summary>
    /// Sets the current standard monthly pay on the fees master screen — the
    /// default a new month's payment starts from, not a payment itself.
    /// </summary>
    [HttpPut("{id:int}/monthly-pay")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateMonthlyPay(
        int id, [FromBody] ReceptionistMonthlyPayRequest request, CancellationToken ct)
        => Envelope(await _receptionists.UpdateMonthlyPayAsync(id, request.MonthlyPay, CurrentUserName, ct),
            "Monthly pay updated.");

    /* ------------------------------------------------------ payments */

    /// <summary>
    /// One person's salary history — the office may look up anyone, and a
    /// signed-in Receptionist may open their own (needed so they can download
    /// their own pay slip from this same dialog); either way a receptionist must
    /// never be handed a colleague's.
    /// </summary>
    [HttpGet("{id:int}/payments")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ReceptionistPaymentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ReceptionistPaymentDto>>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Payments(
        int id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        // A manual IsInRole check, not the declarative [Authorize(Roles=...)]
        // pipeline SuperAdminAuthorizationHandler participates in — so SuperAdmin
        // has to be named explicitly here, the one place in this controller that
        // needs it, rather than relying on the bypass.
        var mayViewAny = User.IsInRole(UserRoles.SuperAdmin)
            || User.IsInRole(UserRoles.Admin) || User.IsInRole(UserRoles.Editor);

        if (!mayViewAny)
        {
            if (!User.IsInRole(UserRoles.Receptionist))
            {
                return Envelope(Result<IReadOnlyList<ReceptionistPaymentDto>>.Failure(
                    "You do not have access to payment history.", ResultStatus.Forbidden));
            }

            var own = await _reports.ResolveOwnReceptionistIdAsync(CurrentUserName ?? string.Empty, ct);

            if (!own.Succeeded)
            {
                return Envelope(Result<IReadOnlyList<ReceptionistPaymentDto>>.Failure(
                    own.Error!, own.Status));
            }

            if (id != own.Value)
            {
                return Envelope(Result<IReadOnlyList<ReceptionistPaymentDto>>.Failure(
                    "You can only view your own payment history.", ResultStatus.Forbidden));
            }
        }

        return Envelope(await _receptionists.GetPaymentsAsync(id, from, to, ct));
    }

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

    /// <summary>
    /// A suggested bonus for covering a colleague's shift, to pre-fill the payment
    /// form with — the Admin still reviews and saves it, nothing is paid on its own.
    /// </summary>
    [HttpGet("{id:int}/extra-shift-bonus")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<ExtraShiftBonusDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ExtraShiftBonus(
        int id, [FromQuery] DateOnly forMonth, CancellationToken ct)
        => Envelope(await _receptionists.GetExtraShiftBonusAsync(id, forMonth, ct));

    /// <summary>Receptionist pay by branch and month.</summary>
    // Everyone's salary on one page. Back office only, for the reason above.
    [HttpGet("report")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<ReceptionistReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ReceptionistReportDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Report(
        [FromQuery] ReceptionistReportQuery query, CancellationToken ct)
        => Envelope(await _receptionists.GetReportAsync(query, ct));

    /* ------------------------------------------------------ leave & deduction */

    /// <summary>
    /// Leave taken against entitlement for every rostered receptionist in one
    /// period, with whatever deduction an Admin has already decided for it.
    /// Back office only — Admin can act on it, Editor can see it.
    /// </summary>
    [HttpGet("leave-report")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<ReceptionistLeaveReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LeaveReport(
        [FromQuery] ReceptionistLeaveReportQuery query, CancellationToken ct)
        => Envelope(await _receptionists.GetLeaveReportAsync(query, ct));

    /// <summary>
    /// Sets (or clears) the deduction percentage for one receptionist's period,
    /// and whether it has actually been applied. Administrator only — it decides
    /// whether money is actually docked from pay.
    /// </summary>
    [HttpPost("leave-deduction")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetLeaveDeduction(
        [FromBody] ReceptionistLeaveDeductionRequest request, CancellationToken ct)
        => Envelope(
            await _receptionists.SetLeaveDeductionAsync(request, CurrentUserName, ct),
            "Deduction decision saved.");
}
