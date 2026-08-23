using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.Expenses;

/// <summary>Filters for the expense register.</summary>
public class ExpenseQuery : PagedQuery
{
    /// <summary>Optional lower bound on the expense date.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Optional upper bound on the expense date.</summary>
    public DateOnly? To { get; set; }
}

public record ExpenseListItemDto(
    int ExpenseId,
    string? Details,
    DateOnly? ExpenseDate,
    decimal Amount);

/// <summary>
/// One expense entry. The legacy form posted three loose strings and inserted them
/// with <c>Insert into Expense values (...)</c> - no column list, no validation and
/// no types.
/// </summary>
public record ExpenseSaveRequest
{
    public string Details { get; init; } = string.Empty;

    /// <summary>Null on create means today, matching what the legacy form defaulted to.</summary>
    public DateOnly? ExpenseDate { get; init; }

    public decimal Amount { get; init; }
}

/// <summary>Totals for whatever the current filter selects.</summary>
public record ExpenseSummaryDto(
    int EntryCount,
    decimal TotalAmount,
    DateOnly? From,
    DateOnly? To);

public record ExpenseListDto(
    PagedResult<ExpenseListItemDto> Rows,
    ExpenseSummaryDto Summary);

public class ExpenseSaveRequestValidator : AbstractValidator<ExpenseSaveRequest>
{
    public ExpenseSaveRequestValidator()
    {
        RuleFor(x => x.Details)
            .NotEmpty().WithMessage("Expense details are required.")
            .MaximumLength(500).WithMessage("Expense details must be 500 characters or fewer.");

        // The column is decimal(9,2): five digits before the point once scale is taken
        // off, so anything from 1,000,000 up cannot be stored.
        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("Amount must be greater than zero.")
            .LessThan(1_000_000m).WithMessage("Amount must be less than 10,00,000.");

        // A date far in the future is a typo, not an expense. The legacy form took
        // whatever the browser parsed, so a mistyped year landed in the table and
        // then quietly fell outside every report range.
        RuleFor(x => x.ExpenseDate)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Now.AddDays(1)))
            .WithMessage("The expense date cannot be in the future.")
            .When(x => x.ExpenseDate.HasValue);
    }
}

public interface IExpenseService
{
    Task<Result<ExpenseListDto>> GetAsync(ExpenseQuery query, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(ExpenseSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(int expenseId, ExpenseSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> DeleteAsync(int expenseId, CancellationToken ct = default);
}
