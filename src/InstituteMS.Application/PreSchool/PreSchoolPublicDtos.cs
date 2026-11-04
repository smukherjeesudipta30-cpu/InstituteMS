using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.PreSchool;

/// <summary>A class as the public PreSchool page shows it: active classes only, fees as totals.</summary>
public record PreSchoolPublicClassDto(
    string Name,
    int? MinAgeMonths,
    int? MaxAgeMonths,
    decimal AdmissionFee,
    decimal MonthlyTotal,
    IReadOnlyList<string> Subjects);

/// <summary>Everything the public PreSchool page needs. No staff names, no audit fields.</summary>
public record PreSchoolPublicDto(
    string SchoolName,
    string? Address,
    string? Phone,
    string? Email,
    string? MapUrl,
    string? UniformShopName,
    string? UniformShopAddress,
    string? UniformShopPhone,
    string? UniformShopMapUrl,
    // The Champahati centre's opening day; the page says "coming soon" until it has passed.
    DateOnly? OpensOn,
    IReadOnlyList<PreSchoolPublicClassDto> Classes,
    // Closures that have not ended yet, soonest first.
    IReadOnlyList<PreSchoolHolidayDto> Holidays);

public record PreSchoolEnquiryRequest
{
    public string ChildName { get; init; } = string.Empty;
    public DateOnly? ChildDateOfBirth { get; init; }
    public string ParentName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? ClassWanted { get; init; }
    public string? Message { get; init; }
}

public class PreSchoolEnquiryRequestValidator : AbstractValidator<PreSchoolEnquiryRequest>
{
    public PreSchoolEnquiryRequestValidator()
    {
        RuleFor(x => x.ChildName).NotEmpty().WithMessage("Enter the child's name.").MaximumLength(100);
        RuleFor(x => x.ParentName).NotEmpty().WithMessage("Enter the parent's name.").MaximumLength(100);
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Enter a phone number.")
            .Matches(@"^\+?[0-9 \-]{10,15}$").WithMessage("Enter a 10-digit phone number.");
        RuleFor(x => x.Email).MaximumLength(150).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Enter a valid email address, or leave it empty.");
        RuleFor(x => x.ClassWanted).MaximumLength(50);
        RuleFor(x => x.Message).MaximumLength(1000);
        RuleFor(x => x.ChildDateOfBirth)
            .Must(d => d is null || (d <= DateOnly.FromDateTime(DateTime.Today) && d >= DateOnly.FromDateTime(DateTime.Today).AddYears(-10)))
            .WithMessage("Check the child's date of birth.");
    }
}

public record PreSchoolEnquiryDto(
    int Id,
    string ChildName,
    DateOnly? ChildDateOfBirth,
    string ParentName,
    string Phone,
    string? Email,
    string? ClassWanted,
    string? Message,
    DateTime CreatedOn,
    bool IsHandled,
    string? HandledBy,
    DateTime? HandledOn);

/// <summary>The public PreSchool page and the admission enquiries it sends.</summary>
public interface IPreSchoolPublicService
{
    Task<PreSchoolPublicDto> GetPublicAsync(CancellationToken ct = default);

    Task<Result<bool>> SubmitEnquiryAsync(PreSchoolEnquiryRequest request, string? clientIp, CancellationToken ct = default);

    /// <summary>Newest first; open ones only unless <paramref name="includeHandled"/>.</summary>
    Task<IReadOnlyList<PreSchoolEnquiryDto>> ListEnquiriesAsync(bool includeHandled, CancellationToken ct = default);

    Task<Result<PreSchoolEnquiryDto>> SetEnquiryHandledAsync(int id, bool handled, string? actor, CancellationToken ct = default);
}
