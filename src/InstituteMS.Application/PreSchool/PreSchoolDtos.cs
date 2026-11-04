using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.PreSchool;

public record PreSchoolSubjectDto(int Id, int ClassId, string Name, int MaxMarks, int SortOrder, bool IsActive);

public record PreSchoolFeeHeadDto(int Id, int ClassId, string Name, decimal MonthlyAmount, int SortOrder, bool IsActive);

/// <summary>A class with its subjects and monthly fee heads. <see cref="MonthlyTotal"/> counts active heads only.</summary>
public record PreSchoolClassDto(
    int Id,
    string Name,
    int SortOrder,
    decimal AdmissionFee,
    int? MinAgeMonths,
    int? MaxAgeMonths,
    bool IsActive,
    decimal MonthlyTotal,
    IReadOnlyList<PreSchoolSubjectDto> Subjects,
    IReadOnlyList<PreSchoolFeeHeadDto> FeeHeads);

public record PreSchoolGradeBandDto(int Id, decimal MinPercent, string Grade, string? Remark);

public record PreSchoolSettingDto(
    string SchoolName,
    string? Address,
    string? Phone,
    string? Email,
    string? MapUrl,
    string? UniformShopName,
    string? UniformShopAddress,
    string? UniformShopPhone,
    string? UniformShopMapUrl,
    string ReceiptPrefix,
    string? ModifiedBy,
    DateTime? ModifiedOn);

/// <summary>Everything the PreSchool dashboard and masters screen show, in one call.</summary>
public record PreSchoolOverviewDto(
    PreSchoolSettingDto Settings,
    IReadOnlyList<PreSchoolClassDto> Classes,
    IReadOnlyList<PreSchoolGradeBandDto> GradeBands);

public record PreSchoolClassSaveRequest
{
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public decimal AdmissionFee { get; init; }
    public int? MinAgeMonths { get; init; }
    public int? MaxAgeMonths { get; init; }
    public bool IsActive { get; init; } = true;
}

public class PreSchoolClassSaveRequestValidator : AbstractValidator<PreSchoolClassSaveRequest>
{
    public PreSchoolClassSaveRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Give the class a name.").MaximumLength(50);
        RuleFor(x => x.AdmissionFee).GreaterThanOrEqualTo(0).WithMessage("Admission fee cannot be negative.");
        RuleFor(x => x.MinAgeMonths).InclusiveBetween(0, 120).When(x => x.MinAgeMonths.HasValue);
        RuleFor(x => x.MaxAgeMonths).InclusiveBetween(0, 120).When(x => x.MaxAgeMonths.HasValue);
        RuleFor(x => x).Must(x => x.MinAgeMonths is null || x.MaxAgeMonths is null || x.MinAgeMonths <= x.MaxAgeMonths)
            .WithMessage("The youngest age must not be above the oldest.");
    }
}

public record PreSchoolSubjectSaveRequest
{
    public int ClassId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int MaxMarks { get; init; } = 100;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public class PreSchoolSubjectSaveRequestValidator : AbstractValidator<PreSchoolSubjectSaveRequest>
{
    public PreSchoolSubjectSaveRequestValidator()
    {
        RuleFor(x => x.ClassId).GreaterThan(0).WithMessage("Pick a class.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Give the subject a name.").MaximumLength(80);
        RuleFor(x => x.MaxMarks).InclusiveBetween(1, 1000).WithMessage("Full marks must be between 1 and 1000.");
    }
}

public record PreSchoolFeeHeadSaveRequest
{
    public int ClassId { get; init; }
    public string Name { get; init; } = string.Empty;
    public decimal MonthlyAmount { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public class PreSchoolFeeHeadSaveRequestValidator : AbstractValidator<PreSchoolFeeHeadSaveRequest>
{
    public PreSchoolFeeHeadSaveRequestValidator()
    {
        RuleFor(x => x.ClassId).GreaterThan(0).WithMessage("Pick a class.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Give the fee a name.").MaximumLength(80);
        RuleFor(x => x.MonthlyAmount).GreaterThanOrEqualTo(0).WithMessage("The amount cannot be negative.");
    }
}

public record PreSchoolGradeBandSaveItem
{
    public decimal MinPercent { get; init; }
    public string Grade { get; init; } = string.Empty;
    public string? Remark { get; init; }
}

/// <summary>The whole grade table, saved at once: bands only make sense together.</summary>
public record PreSchoolGradeBandsSaveRequest
{
    public IReadOnlyList<PreSchoolGradeBandSaveItem> Bands { get; init; } = [];
}

public class PreSchoolGradeBandsSaveRequestValidator : AbstractValidator<PreSchoolGradeBandsSaveRequest>
{
    public PreSchoolGradeBandsSaveRequestValidator()
    {
        RuleFor(x => x.Bands).NotEmpty().WithMessage("Add at least one grade.");
        RuleForEach(x => x.Bands).ChildRules(band =>
        {
            band.RuleFor(b => b.Grade).NotEmpty().WithMessage("Every band needs a grade.").MaximumLength(10);
            band.RuleFor(b => b.MinPercent).InclusiveBetween(0, 100).WithMessage("Percentages run from 0 to 100.");
            band.RuleFor(b => b.Remark).MaximumLength(80);
        });
        RuleFor(x => x.Bands)
            .Must(bands => bands.Select(b => b.Grade.Trim().ToUpperInvariant()).Distinct().Count() == bands.Count)
            .WithMessage("Each grade can appear only once.")
            .Must(bands => bands.Select(b => b.MinPercent).Distinct().Count() == bands.Count)
            .WithMessage("Two grades cannot start at the same percentage.")
            .Must(bands => bands.Count == 0 || bands.Min(b => b.MinPercent) == 0)
            .WithMessage("The lowest grade must start at 0%, so every mark gets a grade.");
    }
}

public record PreSchoolSettingSaveRequest
{
    public string SchoolName { get; init; } = string.Empty;
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? MapUrl { get; init; }
    public string? UniformShopName { get; init; }
    public string? UniformShopAddress { get; init; }
    public string? UniformShopPhone { get; init; }
    public string? UniformShopMapUrl { get; init; }
    public string ReceiptPrefix { get; init; } = "APS";
}

public class PreSchoolSettingSaveRequestValidator : AbstractValidator<PreSchoolSettingSaveRequest>
{
    public PreSchoolSettingSaveRequestValidator()
    {
        RuleFor(x => x.SchoolName).NotEmpty().WithMessage("Give the school's name.").MaximumLength(150);
        RuleFor(x => x.Address).MaximumLength(400);
        RuleFor(x => x.UniformShopAddress).MaximumLength(400);
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.UniformShopPhone).MaximumLength(50);
        RuleFor(x => x.Email).MaximumLength(150).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.UniformShopName).MaximumLength(150);
        RuleFor(x => x.MapUrl).MaximumLength(500).Must(BeWebLink).WithMessage("The map link must start with https://.");
        RuleFor(x => x.UniformShopMapUrl).MaximumLength(500).Must(BeWebLink).WithMessage("The map link must start with https://.");
        RuleFor(x => x.ReceiptPrefix).NotEmpty().MaximumLength(10)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("The receipt prefix can only use letters, digits and '-'.");
    }

    private static bool BeWebLink(string? url) =>
        string.IsNullOrWhiteSpace(url) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The PreSchool's masters: classes, their subjects and monthly fee heads, the
/// grade table and the school's settings. PreSchool admin changes them; the
/// PreSchool's receptionist and teachers read them.
/// </summary>
public interface IPreSchoolMasterService
{
    Task<PreSchoolOverviewDto> GetOverviewAsync(CancellationToken ct = default);

    Task<Result<PreSchoolClassDto>> CreateClassAsync(PreSchoolClassSaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<PreSchoolClassDto>> UpdateClassAsync(int id, PreSchoolClassSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<PreSchoolSubjectDto>> CreateSubjectAsync(PreSchoolSubjectSaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<PreSchoolSubjectDto>> UpdateSubjectAsync(int id, PreSchoolSubjectSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<PreSchoolFeeHeadDto>> CreateFeeHeadAsync(PreSchoolFeeHeadSaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<PreSchoolFeeHeadDto>> UpdateFeeHeadAsync(int id, PreSchoolFeeHeadSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<IReadOnlyList<PreSchoolGradeBandDto>>> SaveGradeBandsAsync(PreSchoolGradeBandsSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<PreSchoolSettingDto>> SaveSettingsAsync(PreSchoolSettingSaveRequest request, string? actor, CancellationToken ct = default);
}
