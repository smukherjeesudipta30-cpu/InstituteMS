using InstituteMS.Application.Common;
using InstituteMS.Application.Income;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Income and profit-and-loss reporting. Replaces <c>IncomeController</c> +
/// <c>Views/Income/Index.cshtml</c> + <c>Scripts/Income.js</c>
/// (MIGRATION_PLAN §5.8).
/// </summary>
/// <remarks>
/// The legacy page had seven independent date ranges, one per panel, each with its
/// own running total and no consolidated figure. Here a single range drives every
/// section and the profit and loss is calculated server-side.
/// </remarks>
[Route("api/v1/income")]
[Tags("Income")]
[Authorize(Roles = UserRoles.Admin)]
public class IncomeController : ApiControllerBase
{
    private readonly IIncomeService _income;

    public IncomeController(IIncomeService income) => _income = income;

    /// <summary>
    /// Consolidated report for one date range: income, cost, net, margin, the
    /// per-section totals and a monthly trend.
    /// </summary>
    /// <remarks>
    /// Late fines are reported as an "of which" breakdown of fee collections rather
    /// than as separate income — <c>MonthlyFees.Installment</c> already includes them.
    /// </remarks>
    [HttpGet("report")]
    [ProducesResponseType(typeof(ApiResponse<IncomeReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IncomeReportDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Report(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Envelope(await _income.GetReportAsync(from, to, ct));

    /// <summary>Paged detail rows behind one section of the report.</summary>
    [HttpGet("detail")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<IncomeDetailDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Detail([FromQuery] IncomeDetailQuery query, CancellationToken ct)
        => Envelope(await _income.GetDetailAsync(query, ct));
}
