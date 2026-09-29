using InstituteMS.Application.Common;
using InstituteMS.Application.Income;
using InstituteMS.Application.Reports;
using InstituteMS.Application.Reports;
using InstituteMS.Domain.Common;
using InstituteMS.Infrastructure.Services;
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
///
/// Admin (and SuperAdmin) see everything. A Receptionist sees the same report to
/// validate it — every amount and total identical — except that the proprietor's
/// honorarium is named "Administrative expenses": the renaming happens here, on
/// the server, so the proprietor's pay is never named in anything sent to them.
/// </remarks>
[Route("api/v1/income")]
[Tags("Income")]
[Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist}")]
public class IncomeController : ApiControllerBase
{
    private readonly IIncomeService _income;
    private readonly IReportService _reports;

    public IncomeController(IIncomeService income, IReportService reports)
    {
        _income = income;
        _reports = reports;
    }

    /// <summary>
    /// Money is reported only for COMPLETED pay cycles: while a cycle is still
    /// running (e.g. 26 Sep to 25 Oct), nothing after the last completed one
    /// (25 Sep) can be reported, so no one reads a part-cycle as final figures.
    /// Returns the refusal message, or null when the range is allowed.
    /// </summary>
    private async Task<string?> PastLastCompletedCycleAsync(DateOnly to, CancellationToken ct)
    {
        var startDay = (await _reports.GetPayrollSettingsAsync(ct)).PayCycleStartDay;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var lastEnd = PayCycles.LastCompletedEnd(today, startDay);
        if (to <= lastEnd) return null;

        var running = PayCycles.Range(PayCycles.LabelMonth(today, startDay), startDay);
        return $"The pay cycle {running.Start:d MMM} to {running.End:d MMM yyyy} is not complete yet. " +
               $"Reports run up to {lastEnd:d MMM yyyy}, the end of the last completed cycle.";
    }

    /// <summary>Admin and SuperAdmin see the honorarium by name; everyone else allowed here does not.</summary>
    private bool SeesProprietorByName =>
        User.IsInRole(UserRoles.SuperAdmin) || User.IsInRole(UserRoles.Admin);

    /// <summary>
    /// Consolidated report for one date range: income, cost, net, margin, the
    /// per-section totals and a monthly trend.
    /// </summary>
    /// <remarks>
    /// <para>Two figures are shown as "of which" breakdowns rather than as separate
    /// lines, because in both cases adding them would count the same money twice:
    /// late fines are already inside <c>MonthlyFees.Installment</c>, and cash paid
    /// to teachers settles the payout already counted when the fee was collected.</para>
    ///
    /// <para><c>branch</c> scopes every figure to one branch; omit it for the whole
    /// institute. Teacher expenses are recorded against a teacher rather than a
    /// branch, so they are the one line a branch-scoped report leaves out.</para>
    /// </remarks>
    [HttpGet("report")]
    [ProducesResponseType(typeof(ApiResponse<IncomeReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IncomeReportDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Report(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string? branch,
        CancellationToken ct)
    {
        if (await PastLastCompletedCycleAsync(to, ct) is { } notYet)
            return Envelope(Result<IncomeReportDto>.Failure(notYet));

        var result = await _income.GetReportAsync(from, to, branch, ct);

        if (result.Succeeded && result.Value is { } report && !SeesProprietorByName)
        {
            // Same amount, same place in the cost total — only the name changes.
            result = Result<IncomeReportDto>.Success(report with
            {
                ProprietorHonorarium = 0m,
                Sections = report.Sections
                    .Select(s => s.Section == IncomeSection.ProprietorHonorarium
                        ? s with { Section = IncomeSection.AdministrativeExpenses, Label = IncomeService.AdministrativeExpensesLabel }
                        : s)
                    .ToList()
            });
        }

        return Envelope(result);
    }

    /// <summary>Paged detail rows behind one section of the report.</summary>
    [HttpGet("detail")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<IncomeDetailDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Detail([FromQuery] IncomeDetailQuery query, CancellationToken ct)
    {
        // A Receptionist reaches these lines only through the neutral section, so
        // they come back named "Administrative expenses" too.
        if (query.Section == IncomeSection.ProprietorHonorarium && !SeesProprietorByName)
            return Forbid();

        if (await PastLastCompletedCycleAsync(query.To, ct) is { } notYet)
            return Envelope(Result<bool>.Failure(notYet));

        return Envelope(await _income.GetDetailAsync(query, ct));
    }
}
