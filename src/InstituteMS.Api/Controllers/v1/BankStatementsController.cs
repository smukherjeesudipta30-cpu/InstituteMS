using InstituteMS.Application.BankStatements;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Monthly stock and book-debt statements for the bank. Admin and Editor
/// (and SuperAdmin, through SuperAdminAuthorizationHandler).
/// </summary>
[Route("api/v1/bank-statements")]
[Tags("Bank statements")]
[Authorize(Roles = UserRoles.Admin + "," + UserRoles.Editor)]
public class BankStatementsController : ApiControllerBase
{
    private readonly IBankStatementService _statements;

    public BankStatementsController(IBankStatementService statements) => _statements = statements;

    /// <summary>One month's statement; a month never saved comes back filled from the latest earlier month.</summary>
    [HttpGet("{year:int}/{month:int}")]
    [ProducesResponseType(typeof(ApiResponse<BankStatementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int year, int month, CancellationToken ct)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            return Envelope(Result<BankStatementDto>.Failure("Pick a valid month."));
        return Envelope(Result<BankStatementDto>.Success(await _statements.GetAsync(year, month, ct)));
    }

    /// <summary>Book debts for the month, worked out from Avijan's fee ledger: unpaid student fees per branch.</summary>
    [HttpGet("{year:int}/{month:int}/book-debts")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BookDebtRowDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> BookDebts(int year, int month, CancellationToken ct)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            return Envelope(Result<IReadOnlyList<BookDebtRowDto>>.Failure("Pick a valid month."));
        return Envelope(Result<IReadOnlyList<BookDebtRowDto>>.Success(await _statements.GetBookDebtsAsync(year, month, ct)));
    }

    [HttpPut("{year:int}/{month:int}")]
    [ProducesResponseType(typeof(ApiResponse<BankStatementDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Save(int year, int month, [FromBody] BankStatementSaveRequest request, CancellationToken ct)
    {
        if (year is < 2000 or > 2100 || month is < 1 or > 12)
            return Envelope(Result<BankStatementDto>.Failure("Pick a valid month."));
        return Envelope(await _statements.SaveAsync(year, month, request.Data, CurrentUserName, ct), "Statement saved.");
    }
}
