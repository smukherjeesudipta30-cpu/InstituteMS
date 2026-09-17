using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.Income;

/// <summary>Filters for the other-income register.</summary>
public class OtherIncomeQuery : PagedQuery
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }

    /// <summary>Empty for every branch.</summary>
    public string? Branch { get; set; }
}

public record OtherIncomeListItemDto(
    int IncomeId,
    string? Purpose,
    DateOnly? IncomeDate,
    decimal Amount,
    string? Branch);

/// <summary>Totals for whatever the current filter selects.</summary>
public record OtherIncomeSummaryDto(
    int EntryCount,
    decimal TotalAmount,
    DateOnly? From,
    DateOnly? To);

public record OtherIncomeListDto(
    PagedResult<OtherIncomeListItemDto> Rows,
    OtherIncomeSummaryDto Summary);

/// <summary>
/// One non-fee income entry — anything the institute earns that is not a student
/// fee or admission fee.
/// </summary>
/// <remarks>
/// <c>dbo.OIncome</c> held 102 migrated rows and had no way to add more: there was
/// no screen, no endpoint and, until recently, no branch column. Recording it here
/// is what lets non-fee income appear in a branch-wise, month-wise income report
/// rather than being frozen at whatever the migration carried over.
/// </remarks>
public record OtherIncomeSaveRequest
{
    public string Purpose { get; init; } = string.Empty;

    /// <summary>Null on create means today, matching the expense form.</summary>
    public DateOnly? IncomeDate { get; init; }

    public decimal Amount { get; init; }

    /// <summary>Required, so every entry reaches the branch report.</summary>
    public string? Branch { get; init; }
}

public class OtherIncomeSaveRequestValidator : AbstractValidator<OtherIncomeSaveRequest>
{
    public OtherIncomeSaveRequestValidator()
    {
        RuleFor(x => x.Purpose)
            .NotEmpty().WithMessage("Say what the income was for.")
            .MaximumLength(200).WithMessage("The purpose must be 200 characters or fewer.");

        RuleFor(x => x.Branch)
            .NotEmpty().WithMessage("Choose the branch that earned this.")
            .MaximumLength(100);

        RuleFor(x => x.Amount)
            .GreaterThan(0m).WithMessage("Amount must be greater than zero.")
            .LessThan(1_000_000m).WithMessage("Amount must be less than 10,00,000.");

        // A date far in the future is a typo, not income — the same guard the
        // expense form carries.
        RuleFor(x => x.IncomeDate)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Now.AddDays(1)))
            .WithMessage("The income date cannot be in the future.")
            .When(x => x.IncomeDate.HasValue);
    }
}

public interface IOtherIncomeService
{
    Task<Result<OtherIncomeListDto>> GetAsync(OtherIncomeQuery query, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(OtherIncomeSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdateAsync(
        int incomeId, OtherIncomeSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> DeleteAsync(int incomeId, CancellationToken ct = default);
}
