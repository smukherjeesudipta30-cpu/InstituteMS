using InstituteMS.Application.Common;
using InstituteMS.Application.Expenses;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The institute expense register. Replaces <c>ExpenseController</c> +
/// <c>Views/Expense/Index.cshtml</c> + <c>Scripts/Expense.js</c>
/// (MIGRATION_PLAN §5.10).
/// </summary>
/// <remarks>
/// The legacy actions were reachable by any signed-in user: only <c>Index</c> and
/// <c>ExpenseReport</c> carried <c>[Authorize(Roles = "Editor,Admin")]</c>, while
/// <c>InserExpenses</c>, <c>Updatechanges</c> and <c>Deletechanges</c> - the three
/// that actually change money records - had no attribute at all. The roles are
/// enforced on the operations here, not on the page that renders them.
/// </remarks>
[Route("api/v1/expenses")]
[Tags("Expenses")]
[Authorize(Roles = UserRoles.BackOffice)]
public class ExpensesController : ApiControllerBase
{
    private readonly IExpenseService _expenses;

    public ExpensesController(IExpenseService expenses) => _expenses = expenses;

    /// <summary>Paged expense register with the totals for the current filter.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ExpenseListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ExpenseListDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get([FromQuery] ExpenseQuery query, CancellationToken ct)
        => Envelope(await _expenses.GetAsync(query, ct));

    /// <summary>Records one expense.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] ExpenseSaveRequest request, CancellationToken ct)
        => Envelope(await _expenses.CreateAsync(request, CurrentUserName, ct), "Expense saved.");

    /// <summary>Updates one expense. Omitting the date keeps the stored one.</summary>
    [HttpPut("{expenseId:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int expenseId, [FromBody] ExpenseSaveRequest request, CancellationToken ct)
        => Envelope(await _expenses.UpdateAsync(expenseId, request, CurrentUserName, ct), "Expense updated.");

    /// <summary>Deletes one expense.</summary>
    [HttpDelete("{expenseId:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int expenseId, CancellationToken ct)
        => Envelope(await _expenses.DeleteAsync(expenseId, ct), "Expense deleted.");
}
