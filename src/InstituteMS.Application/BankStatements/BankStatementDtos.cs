using InstituteMS.Application.Common;

namespace InstituteMS.Application.BankStatements;

/// <summary>
/// A month's statement. When the month has never been saved, <see cref="Data"/>
/// is the latest earlier month's statement and <see cref="CarriedFrom"/> names
/// that month, so Admin only changes what actually changed.
/// </summary>
public record BankStatementDto(string Month, bool Saved, string? CarriedFrom, string Data, string? ModifiedBy, DateTime? ModifiedOn);

public record BankStatementSaveRequest(string Data);

/// <summary>
/// One line of the book-debt statement: the students of one branch with unpaid
/// monthly fees, worked out from the fee ledger as at the month's last day.
/// <see cref="Raised"/> is the fees that fell due in the month, derived as
/// closing − opening + received so the bank's (3+4)−(5)−(6) always holds.
/// </summary>
public record BookDebtRowDto(
    string Drawee,
    int Students,
    decimal Opening,
    decimal Raised,
    decimal Received,
    decimal Returned,
    decimal Net,
    decimal Upto90,
    decimal Days90To120,
    decimal Days120To180,
    decimal Above180);

public interface IBankStatementService
{
    Task<BankStatementDto> GetAsync(int year, int month, CancellationToken ct = default);
    /// <summary>Unpaid student fees per branch as at the month's last day, from the fee ledger.</summary>
    Task<IReadOnlyList<BookDebtRowDto>> GetBookDebtsAsync(int year, int month, CancellationToken ct = default);

    Task<Result<BankStatementDto>> SaveAsync(int year, int month, string data, string? actor, CancellationToken ct = default);
}
