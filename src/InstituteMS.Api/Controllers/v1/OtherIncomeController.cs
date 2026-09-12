using InstituteMS.Application.Common;
using InstituteMS.Application.Income;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The non-fee income register — anything the institute earns that is not a
/// student fee or admission fee.
/// </summary>
/// <remarks>
/// <c>dbo.OIncome</c> was read-only until now: migrated rows with no screen and no
/// endpoint. It feeds the "Other income" line of the income report, so without a
/// way to add to it that line could only shrink into history.
/// </remarks>
// The front desk takes this money over the counter, so entering it is theirs.
// Deleting a recorded income stays with the administrator, below.
[Route("api/v1/other-income")]
[Tags("Other income")]
[Authorize(Roles = UserRoles.MasterData)]
public class OtherIncomeController : ApiControllerBase
{
    private readonly IOtherIncomeService _income;

    public OtherIncomeController(IOtherIncomeService income) => _income = income;

    /// <summary>Paged register with the total over the whole filter.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<OtherIncomeListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<OtherIncomeListDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] OtherIncomeQuery query, CancellationToken ct)
        => Envelope(await _income.GetAsync(query, ct));

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] OtherIncomeSaveRequest request, CancellationToken ct)
        => Envelope(await _income.CreateAsync(request, CurrentUserName, ct), "Income recorded.");

    [HttpPut("{incomeId:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int incomeId, [FromBody] OtherIncomeSaveRequest request, CancellationToken ct)
        => Envelope(await _income.UpdateAsync(incomeId, request, CurrentUserName, ct), "Income updated.");

    /// <summary>Deletes an entry. Administrator only, like deleting an expense.</summary>
    [HttpDelete("{incomeId:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int incomeId, CancellationToken ct)
        => Envelope(await _income.DeleteAsync(incomeId, ct), "Income entry deleted.");
}
