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
[Route("api/v1/fees")]
[Tags("Fees")]
[Authorize(Roles = UserRoles.BackOffice)]
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

    /// <summary>Money receipt for a payment.</summary>
    [HttpGet("payments/{id:int}/receipt")]
    [ProducesResponseType(typeof(ApiResponse<FeeReceiptDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Receipt(int id, CancellationToken ct)
        => Envelope(await _fees.GetReceiptAsync(id, ct));
}
