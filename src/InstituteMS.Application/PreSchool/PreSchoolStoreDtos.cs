using FluentValidation;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Entities;

namespace InstituteMS.Application.PreSchool;

/// <summary>One store item with its stock: everything received, given out, and left.</summary>
public record PreSchoolStoreItemDto(
    int Id,
    string Name,
    int? ClassId,
    string ClassName,
    int SortOrder,
    bool IsActive,
    // The total ever received: the class's capacity for this item.
    int Received,
    int Given,
    int InStock,
    // Active children in the class, and how many of them have not had it yet.
    int Students,
    int StudentsWaiting,
    // The usual price paid per piece, the receipt line parents pay it on, and all that was spent on it.
    decimal? UnitCost = null,
    string? ChargeHead = null,
    decimal Spent = 0);

public record PreSchoolStoreMovementDto(
    int Id,
    int ItemId,
    string ItemName,
    string Kind,
    int Quantity,
    int? StudentId,
    string? StudentName,
    string? AdmissionCode,
    DateOnly OnDate,
    string? Note,
    string? CreatedBy,
    decimal? UnitCost = null,
    string? Supplier = null);

public record PreSchoolStoreDto(
    IReadOnlyList<PreSchoolStoreItemDto> Items,
    // The latest stock received and items given, newest first.
    IReadOnlyList<PreSchoolStoreMovementDto> Recent,
    PreSchoolSupplierAccountDto? Account = null);

public record PreSchoolSupplierPaymentDto(int Id, string Supplier, DateOnly PaidOn, decimal Amount, string Mode, string? Reference, string? Note, string? CreatedBy);

/// <summary>The PreSchool's account with My Chhota School: stock bought, paid, and still owed.</summary>
public record PreSchoolSupplierAccountDto(
    string Supplier,
    // Every delivery entered with a price.
    decimal Bought,
    decimal Paid,
    // Bought less paid: what is still owed (negative means paid in advance).
    decimal Balance,
    IReadOnlyList<PreSchoolSupplierPaymentDto> Payments);

public record PreSchoolSupplierPaymentRequest
{
    public DateOnly? PaidOn { get; init; }
    public decimal Amount { get; init; }
    public string Mode { get; init; } = "Bank";
    public string? Reference { get; init; }
    public string? Note { get; init; }
}

public class PreSchoolSupplierPaymentRequestValidator : AbstractValidator<PreSchoolSupplierPaymentRequest>
{
    public PreSchoolSupplierPaymentRequestValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Enter the amount paid.").LessThan(100_000_000);
        RuleFor(x => x.Mode).Must(m => m is "Cash" or "Bank" or "UPI" or "Cheque").WithMessage("Mode is Cash, Bank, UPI or Cheque.");
        RuleFor(x => x.Reference).MaximumLength(60);
        RuleFor(x => x.Reference).NotEmpty().When(x => x.Mode is "Bank" or "UPI" or "Cheque")
            .WithMessage("Enter the transaction or cheque number.");
        RuleFor(x => x.Note).MaximumLength(300);
        RuleFor(x => x.PaidOn).LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Today))
            .When(x => x.PaidOn.HasValue).WithMessage("The date cannot be in the future.");
    }
}

/// <summary>A child's store items: what their class keeps, and what they have been given.</summary>
public record PreSchoolStudentStoreLineDto(
    int ItemId,
    string ItemName,
    int InStock,
    // The movement that gave it, when it has been given.
    int? IssueId,
    DateOnly? GivenOn,
    string? GivenBy);

public record PreSchoolStoreItemSaveRequest
{
    public string Name { get; init; } = string.Empty;
    public int? ClassId { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public decimal? UnitCost { get; init; }
    public string? ChargeHead { get; init; }
}

public class PreSchoolStoreItemSaveRequestValidator : AbstractValidator<PreSchoolStoreItemSaveRequest>
{
    public PreSchoolStoreItemSaveRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Give the item a name.").MaximumLength(100);
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).When(x => x.UnitCost.HasValue).WithMessage("The price cannot be negative.");
        RuleFor(x => x.ChargeHead).Must(h => h is null or "" || PreSchoolChargeHeads.Labels.ContainsKey(h))
            .WithMessage("Choose the receipt line this item is paid on.");
    }
}

public record PreSchoolStockInRequest
{
    public int Quantity { get; init; }
    public DateOnly? OnDate { get; init; }
    public string? Note { get; init; }

    /// <summary>The price paid per piece; left out, the item's usual price.</summary>
    public decimal? UnitCost { get; init; }

    /// <summary>Left out: My Chhota School.</summary>
    public string? Supplier { get; init; }
}

public class PreSchoolStockInRequestValidator : AbstractValidator<PreSchoolStockInRequest>
{
    public PreSchoolStockInRequestValidator()
    {
        RuleFor(x => x.Quantity).InclusiveBetween(1, 5000).WithMessage("Enter how many came in (1 to 5000).");
        RuleFor(x => x.OnDate).LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Today))
            .When(x => x.OnDate.HasValue).WithMessage("The date cannot be in the future.");
        RuleFor(x => x.Note).MaximumLength(200);
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).When(x => x.UnitCost.HasValue).WithMessage("The price cannot be negative.");
        RuleFor(x => x.Supplier).MaximumLength(100);
    }
}

/// <summary>Tuition books, bags and the like, class by class. PreSchool* tables only.</summary>
public interface IPreSchoolStoreService
{
    Task<PreSchoolStoreDto> GetAsync(CancellationToken ct = default);
    Task<Result<PreSchoolStoreItemDto>> SaveItemAsync(int? id, PreSchoolStoreItemSaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<PreSchoolStoreItemDto>> StockInAsync(int itemId, PreSchoolStockInRequest request, string? actor, CancellationToken ct = default);

    Task<Result<IReadOnlyList<PreSchoolStudentStoreLineDto>>> ForStudentAsync(int studentId, CancellationToken ct = default);
    /// <summary>Gives one of the item to the child, taking it off the class's stock.</summary>
    Task<Result<IReadOnlyList<PreSchoolStudentStoreLineDto>>> GiveAsync(int studentId, int itemId, string? actor, CancellationToken ct = default);

    /// <summary>Removes a movement made in error; the stock goes back as it was.</summary>
    Task<Result<bool>> RemoveMovementAsync(int movementId, CancellationToken ct = default);

    /// <summary>A bulk payment to My Chhota School for store stock.</summary>
    Task<Result<PreSchoolSupplierAccountDto>> AddSupplierPaymentAsync(PreSchoolSupplierPaymentRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Removes a payment entered in error.</summary>
    Task<Result<PreSchoolSupplierAccountDto>> RemoveSupplierPaymentAsync(int paymentId, CancellationToken ct = default);
}
