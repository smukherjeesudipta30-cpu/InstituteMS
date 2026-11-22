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
    string? OfficePhone,
    string? Email,
    string? MapUrl,
    string? UniformShopName,
    string? UniformShopAddress,
    string? UniformShopPhone,
    string? UniformShopMapUrl,
    // The Champahati centre's opening day; the page says "coming soon" until it has passed.
    DateOnly? OpensOn,
    // When the school last put up its own classroom picture; null shows the drawn one.
    DateTime? SmartClassPhotoOn,
    IReadOnlyList<PreSchoolPublicClassDto> Classes,
    // Closures that have not ended yet, soonest first.
    IReadOnlyList<PreSchoolHolidayDto> Holidays);

/// <summary>
/// The school's Enquiry Form. Sent by a parent from the website or the app, or
/// typed in by the office for a walk-in. Older app versions send only the first
/// block (child, ParentName, Phone…); that still works.
/// </summary>
public record PreSchoolEnquiryRequest
{
    public string ChildName { get; init; } = string.Empty;
    public DateOnly? ChildDateOfBirth { get; init; }

    /// <summary>The person to call back. Left empty, it is taken from mother, then father, then guardian.</summary>
    public string? ParentName { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? ClassWanted { get; init; }
    public string? Message { get; init; }

    /// <summary>"2026-27".</summary>
    public string? Session { get; init; }
    public string? Gender { get; init; }

    public string? MotherName { get; init; }
    public string? MotherOccupation { get; init; }
    public string? MotherPhone { get; init; }
    public string? MotherEmail { get; init; }
    public string? MotherAddress { get; init; }

    public string? FatherName { get; init; }
    public string? FatherOccupation { get; init; }
    public string? FatherPhone { get; init; }
    public string? FatherEmail { get; init; }
    public string? FatherAddress { get; init; }

    public string? GuardianName { get; init; }
    public string? GuardianOccupation { get; init; }
    public string? GuardianPhone { get; init; }
    public string? GuardianAddress { get; init; }

    /// <summary>Any of Advertisement, Friends, Facebook, Leaflet, Hoardings, Others.</summary>
    public IReadOnlyList<string>? HeardFrom { get; init; }

    /// <summary>Website or App; the office's own entries are marked Office whatever is sent.</summary>
    public string? Source { get; init; }
}

public static class PreSchoolEnquiryOptions
{
    public static readonly string[] HeardFrom = ["Advertisement", "Friends", "Facebook", "Leaflet", "Hoardings", "Others"];
    public static readonly string[] Genders = ["Female", "Male"];
}

public class PreSchoolEnquiryRequestValidator : AbstractValidator<PreSchoolEnquiryRequest>
{
    private const string PhonePattern = @"^\+?[0-9 \-]{10,15}$";

    public PreSchoolEnquiryRequestValidator()
    {
        RuleFor(x => x.ChildName).NotEmpty().WithMessage("Enter the child's name.").MaximumLength(100);

        RuleFor(x => x).Must(x =>
                (!string.IsNullOrWhiteSpace(x.ParentName) && !string.IsNullOrWhiteSpace(x.Phone)) ||
                (!string.IsNullOrWhiteSpace(x.MotherName) && !string.IsNullOrWhiteSpace(x.MotherPhone)) ||
                (!string.IsNullOrWhiteSpace(x.FatherName) && !string.IsNullOrWhiteSpace(x.FatherPhone)) ||
                (!string.IsNullOrWhiteSpace(x.GuardianName) && !string.IsNullOrWhiteSpace(x.GuardianPhone)))
            .WithName("MotherName")
            .WithMessage("Enter the mother's, father's or guardian's name with a contact number.");

        RuleFor(x => x.Phone).Matches(PhonePattern).When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Enter a 10-digit phone number.");
        RuleFor(x => x.MotherPhone).Matches(PhonePattern).When(x => !string.IsNullOrWhiteSpace(x.MotherPhone))
            .WithMessage("Enter a 10-digit number for the mother.");
        RuleFor(x => x.FatherPhone).Matches(PhonePattern).When(x => !string.IsNullOrWhiteSpace(x.FatherPhone))
            .WithMessage("Enter a 10-digit number for the father.");
        RuleFor(x => x.GuardianPhone).Matches(PhonePattern).When(x => !string.IsNullOrWhiteSpace(x.GuardianPhone))
            .WithMessage("Enter a 10-digit number for the guardian.");

        RuleFor(x => x.Email).MaximumLength(150).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Enter a valid email address, or leave it empty.");
        RuleFor(x => x.MotherEmail).MaximumLength(150).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.MotherEmail))
            .WithMessage("Enter a valid email for the mother, or leave it empty.");
        RuleFor(x => x.FatherEmail).MaximumLength(150).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.FatherEmail))
            .WithMessage("Enter a valid email for the father, or leave it empty.");

        RuleFor(x => x.ParentName).MaximumLength(100);
        RuleFor(x => x.MotherName).MaximumLength(100);
        RuleFor(x => x.FatherName).MaximumLength(100);
        RuleFor(x => x.GuardianName).MaximumLength(100);
        RuleFor(x => x.MotherOccupation).MaximumLength(60);
        RuleFor(x => x.FatherOccupation).MaximumLength(60);
        RuleFor(x => x.GuardianOccupation).MaximumLength(60);
        RuleFor(x => x.MotherAddress).MaximumLength(400);
        RuleFor(x => x.FatherAddress).MaximumLength(400);
        RuleFor(x => x.GuardianAddress).MaximumLength(400);
        RuleFor(x => x.ClassWanted).MaximumLength(50);
        RuleFor(x => x.Message).MaximumLength(1000);
        RuleFor(x => x.Session).Matches(@"^20\d{2}-\d{2}$").When(x => !string.IsNullOrWhiteSpace(x.Session))
            .WithMessage("Write the session like 2026-27.");
        RuleFor(x => x.Gender).Must(g => string.IsNullOrWhiteSpace(g) || PreSchoolEnquiryOptions.Genders.Contains(g))
            .WithMessage("Choose Female or Male.");
        RuleForEach(x => x.HeardFrom).Must(h => PreSchoolEnquiryOptions.HeardFrom.Contains(h))
            .WithMessage("Choose where you heard of the school from the list.");
        RuleFor(x => x.ChildDateOfBirth)
            .Must(d => d is null || (d <= DateOnly.FromDateTime(DateTime.Today) && d >= DateOnly.FromDateTime(DateTime.Today).AddYears(-10)))
            .WithMessage("Check the child's date of birth.");

        // The full Enquiry Form (every current website, app and office form says
        // where it came from). The 2.84 app's short form sends no source and
        // keeps working with the rules above.
        When(x => !string.IsNullOrWhiteSpace(x.Source), () =>
        {
            RuleFor(x => x.Session).NotEmpty().WithMessage("Choose the session.");
            RuleFor(x => x.Gender).NotEmpty().WithMessage("Choose the child's gender.");
            RuleFor(x => x.ChildDateOfBirth).NotNull().WithMessage("Enter the child's date of birth.");
            RuleFor(x => x.ClassWanted).NotEmpty().WithMessage("Choose the class.");
            RuleFor(x => x.HeardFrom).NotEmpty().WithMessage("Tick where you heard of the school.");
            RuleFor(x => x).Must(x =>
                    (Filled(x.MotherName, x.MotherPhone, x.MotherAddress)) ||
                    (Filled(x.FatherName, x.FatherPhone, x.FatherAddress)) ||
                    (Filled(x.GuardianName, x.GuardianPhone, x.GuardianAddress)))
                .WithName("MotherAddress")
                .WithMessage("Enter the mother's, father's or guardian's name, contact number and address.");
        });
    }

    private static bool Filled(params string?[] values) => values.All(v => !string.IsNullOrWhiteSpace(v));
}

public record PreSchoolEnquiryFeedbackRequest
{
    public DateOnly? FeedbackOn { get; init; }
    public string Note { get; init; } = string.Empty;
}

public class PreSchoolEnquiryFeedbackRequestValidator : AbstractValidator<PreSchoolEnquiryFeedbackRequest>
{
    public PreSchoolEnquiryFeedbackRequestValidator()
    {
        RuleFor(x => x.Note).NotEmpty().WithMessage("Write what was discussed.").MaximumLength(1000);
        RuleFor(x => x.FeedbackOn).LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Today))
            .When(x => x.FeedbackOn.HasValue).WithMessage("The date cannot be in the future.");
    }
}

public record PreSchoolEnquiryFeedbackDto(int Id, DateOnly FeedbackOn, string Note, string? CreatedBy);

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
    DateTime? HandledOn,
    string? Session = null,
    string? Gender = null,
    string? MotherName = null,
    string? MotherOccupation = null,
    string? MotherPhone = null,
    string? MotherEmail = null,
    string? MotherAddress = null,
    string? FatherName = null,
    string? FatherOccupation = null,
    string? FatherPhone = null,
    string? FatherEmail = null,
    string? FatherAddress = null,
    string? GuardianName = null,
    string? GuardianOccupation = null,
    string? GuardianPhone = null,
    string? GuardianAddress = null,
    IReadOnlyList<string>? HeardFrom = null,
    string? Source = null,
    string? CreatedBy = null,
    IReadOnlyList<PreSchoolEnquiryFeedbackDto>? Feedback = null);

// <summary>The public PreSchool page and the admission enquiries it sends.</summary>
public interface IPreSchoolPublicService
{
    Task<PreSchoolPublicDto> GetPublicAsync(CancellationToken ct = default);

    Task<Result<bool>> SubmitEnquiryAsync(PreSchoolEnquiryRequest request, string? clientIp, CancellationToken ct = default);

    /// <summary>Newest first; open ones only unless <paramref name="includeHandled"/>.</summary>
    Task<IReadOnlyList<PreSchoolEnquiryDto>> ListEnquiriesAsync(bool includeHandled, CancellationToken ct = default);

    Task<Result<PreSchoolEnquiryDto>> SetEnquiryHandledAsync(int id, bool handled, string? actor, CancellationToken ct = default);

    /// <summary>One enquiry with its feedback notes, for the printed form.</summary>
    Task<Result<PreSchoolEnquiryDto>> GetEnquiryAsync(int id, CancellationToken ct = default);

    /// <summary>A walk-in or phone enquiry typed in by the office.</summary>
    Task<Result<PreSchoolEnquiryDto>> CreateEnquiryAsync(PreSchoolEnquiryRequest request, string? actor, CancellationToken ct = default);

    Task<Result<PreSchoolEnquiryDto>> UpdateEnquiryAsync(int id, PreSchoolEnquiryRequest request, CancellationToken ct = default);

    Task<Result<PreSchoolEnquiryDto>> AddFeedbackAsync(int enquiryId, PreSchoolEnquiryFeedbackRequest request, string? actor, CancellationToken ct = default);

    Task<Result<PreSchoolEnquiryDto>> DeleteFeedbackAsync(int enquiryId, int feedbackId, CancellationToken ct = default);
}
