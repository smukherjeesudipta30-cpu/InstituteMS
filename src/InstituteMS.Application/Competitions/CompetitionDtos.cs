using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.Competitions;

/* ============================================================ the public side */

/// <summary>
/// The advertisement, as an anonymous visitor sees it. Both languages together —
/// the owner asked for the notice "in english and bengali both in screen".
/// </summary>
public record CompetitionPublicDto(
    int CompetitionId,
    int Year,
    DateOnly? EventDate,
    string? Venue,
    string? VenueBengali,

    decimal DrawingFee,
    decimal DanceFee,

    TimeOnly? DrawingStartTime,
    TimeOnly? DanceStartTime,
    TimeOnly? PrizeGivingTime,

    string? NoticeEnglish,
    string? NoticeBengali,
    string? DrawingRulesEnglish,
    string? DrawingRulesBengali,
    string? DanceRulesEnglish,
    string? DanceRulesBengali,

    string? ContactPhones,

    /// <summary>
    /// The institute's UPI id and the name the payer's app shows, so the portal can
    /// draw a QR for the exact amount. Blank disables online payment, exactly as it
    /// does on the student fee page.
    /// </summary>
    string? UpiId,
    string? PayeeName,

    /// <summary>False when registration has closed; the form is then not offered.</summary>
    bool IsOpen,
    DateOnly? RegistrationOpensOn,
    DateOnly? RegistrationClosesOn,

    IReadOnlyList<CompetitionGroupDto> DrawingDivisions,
    IReadOnlyList<CompetitionGroupDto> DanceGroups,
    IReadOnlyList<CompetitionOrganiserDto> Organisers,

    /// <summary>
    /// True when a poster has been uploaded for that competition. The image itself
    /// is fetched from the advert endpoint, which is open to anyone -- it is an
    /// advertisement, and the portal that shows it needs no sign-in.
    /// </summary>
    bool HasDrawingAdvert = false,
    bool HasDanceAdvert = false);

public record CompetitionGroupDto(
    int Id,
    string Kind,
    string Code,
    string LabelEnglish,
    string? LabelBengali,
    int? MinAge,
    int? MaxAge,
    int? MinClass,
    int? MaxClass,

    /// <summary>
    /// How many may enter — 30, 30 and 15 for the dance groups. Null for the
    /// drawing divisions, which are not capped.
    /// </summary>
    int? Capacity,

    int SortOrder);

public record CompetitionOrganiserDto(
    int Id, string Name, string? Designation, string? Phone, int SortOrder, bool IsActive);

/* ------------------------------------------------------------- registering */

public record CompetitionRegisterRequest
{
    public string ParticipantName { get; init; } = string.Empty;
    public string? GuardianName { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public string? SchoolName { get; init; }
    public string? ClassName { get; init; }
    public string Phone { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? Address { get; init; }

    /// <summary>The drawing division chosen, or null when not entering drawing.</summary>
    public int? DrawingGroupId { get; init; }

    /// <summary>The dance group chosen, or null when not entering dance.</summary>
    public int? DanceGroupId { get; init; }

    /// <summary><c>Online</c>, or <c>Venue</c> — which is refused unless drawing alone.</summary>
    public string PaymentMode { get; init; } = "Online";

    /// <summary>Required for an online payment; must not already belong to another entrant.</summary>
    public string? TransactionRef { get; init; }

    /* Documents arrive as Base64 with the form rather than as a second upload step:
       a parent on a phone who loses the connection between the two would otherwise
       have paid and have no registration. */
    public CompetitionUploadDto? Photo { get; init; }
    public CompetitionUploadDto? SchoolId { get; init; }
    public CompetitionUploadDto? BirthCertificate { get; init; }
    public CompetitionUploadDto? PaymentSlip { get; init; }

    /// <summary>The rules have to be read; the form does not open until this is ticked.</summary>
    public bool RulesAccepted { get; init; }

    /// <summary>
    /// The registration code from a save-and-resume draft, if this submission
    /// continues one. Null for someone who filled the whole form in one sitting.
    /// </summary>
    public string? DraftCode { get; init; }
}

public record CompetitionUploadDto(string FileName, string ContentType, string Base64);

/* ---------------------------------------------------------- save and resume */

/// <summary>
/// The quick-start step in front of the full form: a name, a phone and a password
/// the visitor chooses, so they can leave and come back later on any device.
/// </summary>
public record CompetitionDraftStartRequest
{
    public string ParticipantName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

/// <summary>What the visitor is shown once a draft is started — the registration id to keep.</summary>
public record CompetitionDraftStartedDto(string RegistrationCode, string ParticipantName);

public record CompetitionDraftLoginRequest
{
    public string RegistrationCode { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}

/// <summary>Enough to carry a resumed draft back into the full form.</summary>
public record CompetitionDraftDto(
    string RegistrationCode, int CompetitionId, string ParticipantName, string Phone, string Status);

public record CompetitionDraftChangePasswordRequest
{
    public string RegistrationCode { get; init; } = string.Empty;
    public string CurrentPassword { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>Step one of account recovery: which draft has lost its password.</summary>
public record CompetitionDraftForgotPasswordRequest
{
    public string RegistrationCode { get; init; } = string.Empty;
}

/// <summary>Step two: the code just emailed, spent to set a new password.</summary>
public record CompetitionDraftResetPasswordRequest
{
    public string RegistrationCode { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>One row of the office's view of started-but-not-yet-completed registrations.</summary>
public record CompetitionDraftRowDto(
    string RegistrationCode,
    string ParticipantName,
    string Phone,
    string Status,
    DateTime CreatedOn,
    DateTime? LastAccessedOn);

/// <summary>What the entrant is shown, and emailed, once the form is accepted.</summary>
public record CompetitionRegisteredDto(
    int ApplicantId,
    string ParticipantName,
    decimal Amount,
    string PaymentMode,
    string PaymentStatus,
    IReadOnlyList<CompetitionEntryDto> Entries,
    string Message);

public record CompetitionEntryDto(
    string Kind,
    string RegistrationNo,
    string? GroupCode,
    string? GroupLabel,

    /// <summary>The chest number a dancer prints and pins on. Null for drawing.</summary>
    int? BatchNo);

/* ============================================================= the office side */

public class CompetitionReportQuery
{
    /// <summary>Blank for the current year's competition.</summary>
    public int? Year { get; set; }

    /// <summary><c>Drawing</c>, <c>Dance</c>, or blank for both.</summary>
    public string? Kind { get; set; }

    /// <summary><c>Pending</c>, <c>Confirmed</c>, <c>AtVenue</c>, <c>Rejected</c>, or blank.</summary>
    public string? PaymentStatus { get; set; }

    /// <summary>Registration number, participant, guardian or phone.</summary>
    public string? Search { get; set; }
}

public record CompetitionReportRowDto(
    int ApplicantId,
    string RegistrationNo,
    string Kind,
    string? GroupCode,
    string? GroupLabel,

    /// <summary>The dancer's chest number. Null for drawing.</summary>
    int? BatchNo,

    string ParticipantName,
    string? GuardianName,
    DateOnly? DateOfBirth,
    int? Age,
    string? SchoolName,
    string? ClassName,
    string Phone,
    string? Email,

    decimal Amount,
    string PaymentMode,
    string PaymentStatus,
    string? TransactionRef,
    string? CancelReason,
    string? ConfirmedBy,
    DateTime? ConfirmedOn,
    DateTime SubmittedOn,

    bool HasPhoto,
    bool HasSchoolId,
    bool HasBirthCertificate,
    bool HasPaymentSlip);

public record CompetitionReportDto(
    int Year,
    IReadOnlyList<int> AvailableYears,
    IReadOnlyList<CompetitionReportRowDto> Rows,

    int DrawingCount,
    int DanceCount,
    int PendingCount,
    decimal TotalCollected,

    /// <summary>What is still to be taken at the desk on the day.</summary>
    decimal TotalDueAtVenue,

    DateTime GeneratedOn);

/* --------------------------------------------------------- admin settings */

public record CompetitionSaveRequest
{
    public int Year { get; init; }
    public DateOnly? EventDate { get; init; }
    public string? Venue { get; init; }
    public string? VenueBengali { get; init; }

    public decimal DrawingFee { get; init; }
    public decimal DanceFee { get; init; }

    public TimeOnly? DrawingStartTime { get; init; }
    public TimeOnly? DanceStartTime { get; init; }
    public TimeOnly? PrizeGivingTime { get; init; }

    public string? NoticeEnglish { get; init; }
    public string? NoticeBengali { get; init; }
    public string? DrawingRulesEnglish { get; init; }
    public string? DrawingRulesBengali { get; init; }
    public string? DanceRulesEnglish { get; init; }
    public string? DanceRulesBengali { get; init; }

    public DateOnly? RegistrationOpensOn { get; init; }
    public DateOnly? RegistrationClosesOn { get; init; }

    public string? ContactPhones { get; init; }
    public bool IsActive { get; init; } = true;
}

public class CompetitionSaveRequestValidator : AbstractValidator<CompetitionSaveRequest>
{
    public CompetitionSaveRequestValidator()
    {
        RuleFor(x => x.Year)
            .InclusiveBetween(2000, 2100).WithMessage("Enter a four-digit year.");

        RuleFor(x => x.DrawingFee)
            .GreaterThanOrEqualTo(0m).WithMessage("The drawing fee cannot be negative.")
            .LessThan(100000m);

        RuleFor(x => x.DanceFee)
            .GreaterThanOrEqualTo(0m).WithMessage("The dance fee cannot be negative.")
            .LessThan(100000m);

        RuleFor(x => x.Venue).MaximumLength(200);
        RuleFor(x => x.VenueBengali).MaximumLength(200);
        RuleFor(x => x.ContactPhones).MaximumLength(200);

        RuleFor(x => x.RegistrationClosesOn)
            .GreaterThanOrEqualTo(x => x.RegistrationOpensOn!.Value)
            .WithMessage("Registration cannot close before it opens.")
            .When(x => x.RegistrationOpensOn.HasValue && x.RegistrationClosesOn.HasValue);
    }
}

public record CompetitionOrganiserSaveRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Designation { get; init; }
    public string? Phone { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

public class CompetitionOrganiserSaveRequestValidator : AbstractValidator<CompetitionOrganiserSaveRequest>
{
    public CompetitionOrganiserSaveRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Enter the name shown on the form.")
            .MaximumLength(150);

        RuleFor(x => x.Designation).MaximumLength(150);
        RuleFor(x => x.Phone).MaximumLength(20);
    }
}

/// <summary>
/// The chest number sheet a dancer downloads once the office has confirmed their
/// payment.
/// </summary>
public record ChestCardDto(
    string RegistrationNo,
    string ParticipantName,
    int BatchNo,
    string? GroupLabel,
    int Year,
    DateOnly? EventDate,
    string? Venue);

/// <summary>Builds the printable chest number sheet.</summary>
public interface IChestCardPdfGenerator
{
    byte[] Build(ChestCardDto card);
}

/// <summary>One document, fetched for the office to look at.</summary>
public record CompetitionDocumentDto(byte[] Content, string ContentType, string FileName);

/* =================================================================== service */

public interface ICompetitionService
{
    /// <summary>
    /// The advertisement for the year now open, or null when nothing is open — in
    /// which case the landing page shows no banner at all.
    /// </summary>
    Task<CompetitionPublicDto?> GetPublicAsync(CancellationToken ct = default);

    /// <summary>Takes one public registration and issues its numbers.</summary>
    Task<Result<CompetitionRegisteredDto>> RegisterAsync(
        CompetitionRegisterRequest request, string? sourceIp, CancellationToken ct = default);

    /* --------------------------------------------------- save and resume */

    /// <summary>
    /// The quick-start step behind "Register now": issues a registration code and
    /// notifies the office, but reserves no seat and consumes no capacity.
    /// </summary>
    Task<Result<CompetitionDraftStartedDto>> StartDraftAsync(
        CompetitionDraftStartRequest request, CancellationToken ct = default);

    /// <summary>Resumes a draft with its registration code and password.</summary>
    Task<Result<CompetitionDraftDto>> ResumeDraftAsync(
        CompetitionDraftLoginRequest request, CancellationToken ct = default);

    Task<Result<bool>> ChangeDraftPasswordAsync(
        CompetitionDraftChangePasswordRequest request, CancellationToken ct = default);

    /// <summary>
    /// Emails a reset code for a draft that has an address on file. Always succeeds
    /// from the caller's point of view — whether the code matched a draft, and
    /// whether that draft has an email at all, is not the response's to reveal.
    /// </summary>
    Task<Result<bool>> ForgotDraftPasswordAsync(
        CompetitionDraftForgotPasswordRequest request, CancellationToken ct = default);

    /// <summary>Spends the emailed code to set a new draft password without the old one.</summary>
    Task<Result<bool>> ResetDraftPasswordAsync(
        CompetitionDraftResetPasswordRequest request, CancellationToken ct = default);

    /// <summary>Every draft for the year's competition, started or completed — the office's follow-up list.</summary>
    Task<Result<IReadOnlyList<CompetitionDraftRowDto>>> GetDraftsAsync(
        int? competitionId, CancellationToken ct = default);

    /* ------------------------------------------------------------- office */

    Task<Result<CompetitionReportDto>> GetReportAsync(
        CompetitionReportQuery query, CancellationToken ct = default);

    /// <summary>
    /// Confirms or rejects a payment. The administrator does this from the dashboard
    /// after checking the account, exactly as they confirm a student's online fee —
    /// nothing here can ask the bank.
    /// </summary>
    Task<Result<bool>> SetPaymentStatusAsync(
        int applicantId, string status, string? actor, CancellationToken ct = default);

    /// <summary>
    /// Cancels a registration and refunds it in full — the whole amount, for both
    /// competitions. The dance place and the chest number go back to the group.
    /// </summary>
    Task<Result<bool>> CancelAsync(
        int applicantId, string? reason, string? actor, CancellationToken ct = default);

    /// <summary>
    /// The chest number sheet for one dance registration, as a PDF.
    /// </summary>
    /// <remarks>
    /// Anonymous, so the entrant needs no account -- they identify themselves with
    /// their registration number and the phone number on the form, which is what
    /// they have. Refused until the office has confirmed the payment.
    /// </remarks>
    Task<Result<CompetitionDocumentDto>> GetChestCardAsync(
        string registrationNo, string phone, CancellationToken ct = default);

    /// <summary>Uploads or replaces one of the two posters. Admin only.</summary>
    Task<Result<bool>> SetAdvertAsync(
        int competitionId, string slot, byte[] content, string fileName, string contentType,
        int? width, int? height, string? actor, CancellationToken ct = default);

    /// <summary>One poster, for the public portal.</summary>
    Task<Result<CompetitionDocumentDto>> GetAdvertAsync(
        int competitionId, string slot, CancellationToken ct = default);

    Task<Result<bool>> RemoveAdvertAsync(
        int competitionId, string slot, CancellationToken ct = default);

    Task<Result<CompetitionDocumentDto>> GetDocumentAsync(
        int applicantId, string kind, CancellationToken ct = default);

    /* ----------------------------------------------------------- settings */

    Task<IReadOnlyList<CompetitionPublicDto>> GetAllAsync(CancellationToken ct = default);

    Task<Result<int>> SaveCompetitionAsync(
        int? id, CompetitionSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<int>> SaveOrganiserAsync(
        int competitionId, int? organiserId, CompetitionOrganiserSaveRequest request,
        CancellationToken ct = default);

    Task<Result<bool>> DeleteOrganiserAsync(int organiserId, CancellationToken ct = default);
}
