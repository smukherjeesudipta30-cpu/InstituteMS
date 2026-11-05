using FluentValidation;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Entities;

namespace InstituteMS.Application.PreSchool;

public record PreSchoolStudentListItemDto(
    int Id,
    string AdmissionCode,
    string ChildName,
    int ClassId,
    string ClassName,
    string ParentName,
    string Phone,
    DateOnly JoinedOn,
    string Status,
    decimal MonthlyFee,
    int MonthsDue,
    // The admission fee when it has not been taken yet; zero once it has.
    decimal AdmissionDue,
    // Unpaid months plus any admission fee still due.
    decimal AmountDue);

/// <summary>One month of a child's fees: paid (with the receipt) or still due.</summary>
public record PreSchoolMonthDto(DateOnly Month, bool IsPaid, decimal Amount, string? ReceiptCode);

public record PreSchoolReceiptSummaryDto(int Id, string ReceiptCode, DateOnly ReceivedOn, decimal Total, string Mode, bool IsCancelled, string Summary);

public record PreSchoolStudentDetailDto(
    int Id,
    string AdmissionCode,
    string ChildName,
    DateOnly DateOfBirth,
    string? Gender,
    string? BloodGroup,
    string? FatherName,
    string? MotherName,
    string ParentName,
    string Phone,
    string? EmergencyPhone,
    string? Email,
    string? Address,
    string? GuardianRelation,
    string? Guardian2Name,
    string? Guardian2Relation,
    string? Guardian2Phone,
    decimal? HeightCm,
    decimal? WeightKg,
    int ClassId,
    string ClassName,
    DateOnly JoinedOn,
    decimal MonthlyDiscount,
    string Status,
    DateOnly? EndedOn,
    string? Notes,
    // The class's monthly fee less the discount: what one month costs this child.
    decimal MonthlyFee,
    decimal AdmissionFee,
    bool AdmissionPaid,
    IReadOnlyList<PreSchoolMonthDto> Months,
    IReadOnlyList<PreSchoolReceiptSummaryDto> Receipts);

public record PreSchoolStudentSaveRequest
{
    public string ChildName { get; init; } = string.Empty;
    public DateOnly DateOfBirth { get; init; }
    public string? Gender { get; init; }
    public string? BloodGroup { get; init; }
    public string? FatherName { get; init; }
    public string? MotherName { get; init; }
    public string ParentName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? EmergencyPhone { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? GuardianRelation { get; init; }
    public string? Guardian2Name { get; init; }
    public string? Guardian2Relation { get; init; }
    public string? Guardian2Phone { get; init; }
    public decimal? HeightCm { get; init; }
    public decimal? WeightKg { get; init; }
    public int ClassId { get; init; }
    public DateOnly JoinedOn { get; init; }
    public decimal MonthlyDiscount { get; init; }
    public string Status { get; init; } = PreSchoolStudentStatus.Active;
    public DateOnly? EndedOn { get; init; }
    public string? Notes { get; init; }
}

public class PreSchoolStudentSaveRequestValidator : AbstractValidator<PreSchoolStudentSaveRequest>
{
    private const string PhonePattern = @"^\+?[0-9 \-]{10,15}$";

    public PreSchoolStudentSaveRequestValidator()
    {
        RuleFor(x => x.ChildName).NotEmpty().WithMessage("Enter the child's name.").MaximumLength(100);
        RuleFor(x => x.DateOfBirth)
            .Must(d => d != default && d <= DateOnly.FromDateTime(DateTime.Today) && d >= DateOnly.FromDateTime(DateTime.Today).AddYears(-10))
            .WithMessage("Check the child's date of birth.");
        RuleFor(x => x.Gender).Must(g => g is null or "" or "Boy" or "Girl").WithMessage("Gender is Boy or Girl.");
        RuleFor(x => x.BloodGroup).MaximumLength(5);
        RuleFor(x => x.FatherName).MaximumLength(100);
        RuleFor(x => x.MotherName).MaximumLength(100);
        RuleFor(x => x.ParentName).NotEmpty().WithMessage("Enter the parent's name.").MaximumLength(100);
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Enter the parent's phone.")
            .Matches(PhonePattern).WithMessage("Enter a 10-digit phone number.");
        RuleFor(x => x.EmergencyPhone).Matches(PhonePattern).When(x => !string.IsNullOrWhiteSpace(x.EmergencyPhone))
            .WithMessage("Enter a 10-digit emergency phone, or leave it empty.");
        RuleFor(x => x.Email).MaximumLength(150).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Enter a valid email address, or leave it empty.");
        RuleFor(x => x.Address).NotEmpty().WithMessage("Enter the home address.").MaximumLength(400);
        RuleFor(x => x.GuardianRelation).MaximumLength(30);
        RuleFor(x => x.Guardian2Name).MaximumLength(100);
        RuleFor(x => x.Guardian2Relation).MaximumLength(30);
        RuleFor(x => x.Guardian2Phone).Matches(PhonePattern).When(x => !string.IsNullOrWhiteSpace(x.Guardian2Phone))
            .WithMessage("Enter a 10-digit phone for the second guardian, or leave it empty.");
        RuleFor(x => x.Guardian2Name).NotEmpty().When(x => !string.IsNullOrWhiteSpace(x.Guardian2Phone))
            .WithMessage("Enter the second guardian's name with their phone.");
        // A pre-schooler is roughly 60–140 cm and 5–40 kg; the wider limits only catch typing slips.
        RuleFor(x => x.HeightCm).InclusiveBetween(40, 200).When(x => x.HeightCm.HasValue)
            .WithMessage("Check the height: it is in centimetres.");
        RuleFor(x => x.WeightKg).InclusiveBetween(2, 80).When(x => x.WeightKg.HasValue)
            .WithMessage("Check the weight: it is in kilograms.");
        RuleFor(x => x.ClassId).GreaterThan(0).WithMessage("Pick a class.");
        RuleFor(x => x.JoinedOn).NotEqual(default(DateOnly)).WithMessage("Pick the joining date.")
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Today).AddMonths(3))
            .WithMessage("The joining date cannot be more than 3 months ahead.");
        RuleFor(x => x.MonthlyDiscount).GreaterThanOrEqualTo(0).WithMessage("The discount cannot be negative.");
        RuleFor(x => x.Status).Must(s => PreSchoolStudentStatus.All.Contains(s))
            .WithMessage("Status is Active, Completed or Left.");
        RuleFor(x => x.EndedOn).NotNull().When(x => x.Status != PreSchoolStudentStatus.Active)
            .WithMessage("Give the date the child completed the class or left.");
        RuleFor(x => x).Must(x => x.EndedOn is null || x.EndedOn >= x.JoinedOn)
            .WithMessage("The end date cannot be before the joining date.");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

/// <summary>Money received from a parent: the admission fee, months, or both.</summary>
public record PreSchoolCollectRequest
{
    public bool IncludeAdmission { get; init; }

    /// <summary>When left out, the class's admission fee.</summary>
    public decimal? AdmissionAmount { get; init; }

    /// <summary>The months paid for, as their first day.</summary>
    public IReadOnlyList<DateOnly> Months { get; init; } = [];

    public string Mode { get; init; } = "Cash";
    public string? Reference { get; init; }

    /// <summary>When left out, today.</summary>
    public DateOnly? ReceivedOn { get; init; }

    public string? Notes { get; init; }
}

public class PreSchoolCollectRequestValidator : AbstractValidator<PreSchoolCollectRequest>
{
    public PreSchoolCollectRequestValidator()
    {
        RuleFor(x => x).Must(x => x.IncludeAdmission || x.Months.Count > 0)
            .WithMessage("Tick the admission fee or at least one month.");
        RuleFor(x => x.Months.Count).LessThanOrEqualTo(24).WithMessage("At most 24 months on one receipt.");
        RuleFor(x => x.AdmissionAmount).GreaterThan(0).When(x => x.AdmissionAmount.HasValue)
            .WithMessage("The admission amount must be above zero.");
        RuleFor(x => x.Mode).Must(m => m is "Cash" or "UPI" or "Paytm").WithMessage("Payment mode is Cash, UPI or Paytm.");
        RuleFor(x => x.Reference).NotEmpty().When(x => x.Mode is "UPI" or "Paytm")
            .WithMessage("Enter the UPI or Paytm reference number.").MaximumLength(60);
        RuleFor(x => x.ReceivedOn).LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Today))
            .When(x => x.ReceivedOn.HasValue).WithMessage("The payment date cannot be in the future.");
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public record PreSchoolReceiptLineDto(string Kind, DateOnly? ForMonth, string Description, decimal Amount);

/// <summary>A receipt with everything printed on it.</summary>
public record PreSchoolReceiptDto(
    int Id,
    string ReceiptCode,
    DateOnly ReceivedOn,
    string Mode,
    string? Reference,
    decimal Total,
    string TotalInWords,
    string? Notes,
    bool IsCancelled,
    string? ReceivedBy,
    int StudentId,
    string AdmissionCode,
    string ChildName,
    string ClassName,
    string ParentName,
    string Phone,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolPhone,
    IReadOnlyList<PreSchoolReceiptLineDto> Lines);

public record PreSchoolPendingRowDto(
    int StudentId,
    string AdmissionCode,
    string ChildName,
    string ClassName,
    string ParentName,
    string Phone,
    IReadOnlyList<DateOnly> Months,
    decimal MonthlyFee,
    decimal AdmissionDue,
    decimal Amount);

public record PreSchoolPendingReportDto(
    IReadOnlyList<PreSchoolPendingRowDto> Rows,
    int Children,
    int Months,
    decimal Total);

public record PreSchoolAdmissionRowDto(
    int StudentId,
    string AdmissionCode,
    string ChildName,
    string ClassName,
    string ParentName,
    string Phone,
    DateOnly JoinedOn,
    string Status,
    decimal AdmissionFeeReceived);

/// <summary>One month of the year: how many children joined, by class, with the admission fees taken.</summary>
public record PreSchoolAdmissionMonthDto(
    DateOnly Month,
    int Count,
    decimal AdmissionFees,
    IReadOnlyDictionary<string, int> ByClass,
    IReadOnlyList<PreSchoolAdmissionRowDto> Children);

public record PreSchoolAdmissionReportDto(int Year, int Total, decimal AdmissionFees, IReadOnlyList<PreSchoolAdmissionMonthDto> Months);

/// <summary>A child's three ID-card photos, as data URLs; null where none has been taken.</summary>
public record PreSchoolStudentPhotosDto(string? Child, string? Guardian1, string? Guardian2);

/// <summary>A photo as a data URL ("data:image/jpeg;base64,…"), or null to remove it.</summary>
public record PreSchoolPhotoSaveRequest
{
    public string? DataUrl { get; init; }
}

/// <summary>Photos for the ID cards and the landing page's classroom picture.</summary>
public interface IPreSchoolPhotoService
{
    Task<Result<PreSchoolStudentPhotosDto>> GetStudentPhotosAsync(int studentId, CancellationToken ct = default);
    Task<Result<PreSchoolStudentPhotosDto>> SaveStudentPhotoAsync(int studentId, string slot, PreSchoolPhotoSaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>The landing page's classroom picture, or null while the school has not put one up.</summary>
    Task<(byte[] Data, string ContentType, DateTime UpdatedOn)?> GetSitePhotoAsync(string slot, CancellationToken ct = default);
    Task<Result<DateTime?>> SaveSitePhotoAsync(string slot, PreSchoolPhotoSaveRequest request, string? actor, CancellationToken ct = default);
}

/// <summary>PreSchool admissions, fees and receipts. Nothing here reads a Computer Centre table.</summary>
public interface IPreSchoolStudentService
{
    Task<IReadOnlyList<PreSchoolStudentListItemDto>> ListAsync(string? search, int? classId, string? status, CancellationToken ct = default);
    Task<Result<PreSchoolStudentDetailDto>> GetAsync(int id, CancellationToken ct = default);
    Task<Result<PreSchoolStudentDetailDto>> AdmitAsync(PreSchoolStudentSaveRequest request, string? actor, CancellationToken ct = default);
    Task<Result<PreSchoolStudentDetailDto>> UpdateAsync(int id, PreSchoolStudentSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<PreSchoolReceiptDto>> CollectAsync(int studentId, PreSchoolCollectRequest request, string? actor, CancellationToken ct = default);
    Task<Result<PreSchoolReceiptDto>> GetReceiptAsync(int receiptId, CancellationToken ct = default);
    Task<Result<PreSchoolReceiptDto>> CancelReceiptAsync(int receiptId, string? actor, CancellationToken ct = default);

    /// <summary>Admissions in a calendar year, month by month, by joining date.</summary>
    Task<PreSchoolAdmissionReportDto> AdmissionsAsync(int year, int? classId, CancellationToken ct = default);

    /// <summary>Children with unpaid months up to this month (and admission fees not taken).</summary>
    Task<PreSchoolPendingReportDto> PendingAsync(int? classId, int minMonths, CancellationToken ct = default);
}
