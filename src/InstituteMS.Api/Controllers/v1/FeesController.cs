using InstituteMS.Application.Common;
using InstituteMS.Application.Fees;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Monthly course fees. Replaces <c>MonthlyCourseFeesController</c> +
/// <c>Views/MonthlyCourseFees/Index.cshtml</c> + <c>Scripts/MonthlyCourse.js</c>
/// (MIGRATION_PLAN §5.5).
/// </summary>
/// <remarks>
/// Registration ids contain slashes, so every endpoint keyed by one takes it as a
/// query parameter rather than a route segment.
/// </remarks>
/* Taking the monthly fee at the counter is the receptionist's job, so the whole
   controller is MasterData — except confirming an ONLINE payment, which is a
   different act: it means someone has checked the institute's Paytm account and
   agreed the money arrived. That stays with the back office below. */
[Route("api/v1/fees")]
[Tags("Fees")]
[Authorize(Roles = UserRoles.MasterData)]
public class FeesController : ApiControllerBase
{
    private readonly IFeeService _fees;

    public FeesController(IFeeService fees) => _fees = fees;

    /// <summary>
    /// The "Go" button. Returns the admission details plus the three figures the form
    /// calculates from: installments already paid, last payment date, outstanding due.
    /// </summary>
    [HttpGet("bill")]
    [ProducesResponseType(typeof(ApiResponse<StudentFeeBillDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Bill([FromQuery] string registrationId, CancellationToken ct)
        => Envelope(await _fees.GetBillAsync(registrationId, ct));

    /// <summary>Payment history for a student, newest first.</summary>
    [HttpGet("payments")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<FeePaymentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Payments(
        [FromQuery] string registrationId, [FromQuery] PagedQuery query, CancellationToken ct)
        => Envelope(await _fees.GetPaymentsAsync(registrationId, query, ct));

    /// <summary>
    /// Works out days elapsed, late fine, total for the month and the new due.
    /// The legacy app did this in JavaScript; running it here keeps the saved figures
    /// and the displayed ones in step, and gives the same answer to any client.
    /// </summary>
    [HttpPost("calculate")]
    [ProducesResponseType(typeof(ApiResponse<FeeCalculationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Calculate([FromBody] FeeCalculationRequest request, CancellationToken ct)
        => Envelope(await _fees.CalculateAsync(request, ct));

    /// <summary>
    /// Records a payment. Writes the ledger row, the income roll-up, the late-fine row
    /// and one teacher-payout row per assigned teacher, all in one transaction —
    /// the same four tables the legacy <c>usp_SaveData</c> touched.
    /// </summary>
    [HttpPost("payments")]
    [ProducesResponseType(typeof(ApiResponse<FeeRecordedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Record([FromBody] FeeSaveRequest request, CancellationToken ct)
        => Envelope(await _fees.RecordPaymentAsync(request, CurrentUserName, ct), "Payment saved.");

    /// <summary>Amends a recorded payment. The bill number is immutable.</summary>
    [HttpPut("payments/{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] FeeSaveRequest request, CancellationToken ct)
        => Envelope(await _fees.UpdatePaymentAsync(id, request, CurrentUserName, ct), "Payment updated.");

    /// <summary>
    /// Removes a payment entered by mistake or duplicated. Administrator only,
    /// like every other delete in this ledger — reading and correcting a fee is
    /// the office's job, undoing one outright is the administrator's call.
    /// </summary>
    [HttpDelete("payments/{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => Envelope(await _fees.DeletePaymentAsync(id, ct), "Payment removed.");

    /// <summary>Money receipt for a payment.</summary>
    [HttpGet("payments/{id:int}/receipt")]
    [ProducesResponseType(typeof(ApiResponse<FeeReceiptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Receipt(int id, CancellationToken ct)
        => Envelope(await _fees.GetReceiptAsync(id, ct));

    /* ------------------------------------- online payment (back office) */

    /// <summary>
    /// Online payments recorded from the portal but not yet checked against the
    /// institute's Paytm account — the dashboard's notification list.
    /// </summary>
    [HttpGet("online/unverified")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<OnlinePaymentNoticeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnverifiedOnline([FromQuery] int take, CancellationToken ct)
        => Envelope(await _fees.GetUnverifiedOnlinePaymentsAsync(take <= 0 ? 25 : take, ct));

    /// <summary>Marks an online payment as checked and confirmed.</summary>
    [HttpPost("online/{feeId:int}/verify")]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyOnline(int feeId, CancellationToken ct)
        => Envelope(
            await _fees.VerifyOnlinePaymentAsync(feeId, CurrentUserName, ct),
            "Payment confirmed.");
}

/// <summary>
/// A student paying their own monthly fee from the portal, against the institute's
/// shared UPI QR code.
/// </summary>
/// <remarks>
/// Student role only, and the student is resolved from the signed-in email — there
/// is no registration id to pass, so no one can pay against someone else's ledger
/// or read what another student owes.
/// </remarks>
[Route("api/v1/my-fees")]
[Tags("Fees")]
[Authorize(Roles = UserRoles.Student)]
public class MyFeesController : ApiControllerBase
{
    private readonly IFeeService _fees;

    public MyFeesController(IFeeService fees) => _fees = fees;

    /// <summary>
    /// What this student owes for their next unpaid month — the fee, the late fine
    /// worked out by the same calculator the counter uses, and the UPI details.
    /// </summary>
    [HttpGet("due")]
    [ProducesResponseType(typeof(ApiResponse<MyFeeDueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MyDue(CancellationToken ct)
        => Envelope(await _fees.GetMyDueAsync(CurrentUserName ?? string.Empty, ct));

    /// <summary>
    /// Records the fee the student has just paid against the QR code.
    /// </summary>
    /// <remarks>
    /// The amount is taken from the server's own calculation, never from the
    /// request — the browser sends only the UPI reference, so a student cannot
    /// choose what their month costs.
    ///
    /// <para>The QR is static and there is no gateway callback, so this records the
    /// payment <b>unverified</b>. The office confirms it from the dashboard against
    /// the Paytm account.</para>
    /// </remarks>
    [HttpPost("pay")]
    [ProducesResponseType(typeof(ApiResponse<FeeRecordedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<FeeRecordedDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<FeeRecordedDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Pay(
        [FromBody] OnlineFeePaymentRequest request, CancellationToken ct)
        => Envelope(
            await _fees.PayOnlineAsync(CurrentUserName ?? string.Empty, request, ct),
            "Payment recorded. The office will confirm it shortly.");
}
