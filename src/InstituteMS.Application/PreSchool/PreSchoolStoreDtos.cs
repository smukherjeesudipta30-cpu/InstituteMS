using FluentValidation;
using InstituteMS.Application.Common;

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
    int StudentsWaiting);

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
    string? CreatedBy);

public record PreSchoolStoreDto(
    IReadOnlyList<PreSchoolStoreItemDto> Items,
    // The latest stock received and items given, newest first.
    IReadOnlyList<PreSchoolStoreMovementDto> Recent);

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
}

public class PreSchoolStoreItemSaveRequestValidator : AbstractValidator<PreSchoolStoreItemSaveRequest>
{
    public PreSchoolStoreItemSaveRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Give the item a name.").MaximumLength(100);
    }
}

public record PreSchoolStockInRequest
{
    public int Quantity { get; init; }
    public DateOnly? OnDate { get; init; }
    public string? Note { get; init; }
}

public class PreSchoolStockInRequestValidator : AbstractValidator<PreSchoolStockInRequest>
{
    public PreSchoolStockInRequestValidator()
    {
        RuleFor(x => x.Quantity).InclusiveBetween(1, 5000).WithMessage("Enter how many came in (1 to 5000).");
        RuleFor(x => x.OnDate).LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Today))
            .When(x => x.OnDate.HasValue).WithMessage("The date cannot be in the future.");
        RuleFor(x => x.Note).MaximumLength(200);
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
}
