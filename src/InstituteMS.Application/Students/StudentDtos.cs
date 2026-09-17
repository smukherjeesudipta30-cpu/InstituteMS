using InstituteMS.Application.Common;

namespace InstituteMS.Application.Students;

/// <summary>One row of the student list — mirrors the legacy <c>CoName.Students()</c> projection.</summary>
public record StudentListItemDto(
    int Id,
    string? Name,
    string? GuardianName,
    string? RegistrationId,
    string? BillNo,
    string? Phone,
    string? Email,
    string? Address,
    int? CourseId,
    string? CourseName,
    int? CourseDuration,
    int? TeacherCount,
    int? PrimaryTeacherId,
    string? PrimaryTeacherName,
    int? SecondTeacherId,
    string? SecondTeacherName,
    int? ThirdTeacherId,
    string? ThirdTeacherName,
    decimal? AdmissionFees,
    string? AdmissionPaymentMode,
    decimal? MonthlyFees,
    DateOnly? DateOfJoining,
    DateOnly? DateOfBirth,
    DateOnly? DateOfExit,
    bool IsActive,
    bool IsComplete,
    int? BranchId,
    string? Branch,
    bool HasPhoto,

    /// <summary>
    /// How much of the admission fee has come in. Defaulted so the many other
    /// places constructing this row positionally did not all have to change.
    /// </summary>
    decimal? AdmissionFeesPaid = null,

    /// <summary>Still owed on the admission fee, floored at zero.</summary>
    decimal AdmissionDue = 0m,

    /// <summary>
    /// The student's own address — where receipts and reset codes go. Was missing
    /// from this row, so the edit form had nothing to load and saved back empty,
    /// silently erasing it on every edit.
    /// </summary>
    string? PersonalEmail = null);

/// <summary>Lightweight row for dropdowns / the re-registration picker.</summary>
public record StudentLookupDto(
    int Id,
    string? Name,
    string? RegistrationId,
    string? Phone,
    string? CourseName);

/// <summary>Create + update payload. Field names follow the new API, not the legacy column names.</summary>
public record StudentSaveRequest
{
    public string? Name { get; init; }
    public string? GuardianName { get; init; }
    public string? Address { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    public DateOnly? DateOfJoining { get; init; }
    public decimal? MonthlyFees { get; init; }
    public decimal? AdmissionFees { get; init; }

    /// <summary>
    /// How much of <see cref="AdmissionFees"/> is actually being handed over now.
    /// Null means the whole thing, which is what every admission did before part
    /// payment existed — so an older client that does not send this field still
    /// books a fully-paid admission rather than silently creating a due.
    /// </summary>
    public decimal? AdmissionFeesPaid { get; init; }

    /// <summary><c>Cash</c> or <c>Bank</c> — how the admission fee was received.</summary>
    public string AdmissionPaymentMode { get; init; } = "Cash";
    public int? CourseId { get; init; }
    /// <summary>1-3. Drives which teacher slots are required.</summary>
    public int? TeacherCount { get; init; }
    public int? PrimaryTeacherId { get; init; }
    public int? SecondTeacherId { get; init; }
    public int? ThirdTeacherId { get; init; }
    public bool IsActive { get; init; }
    public bool IsComplete { get; init; }
    /// <summary>
    /// Chosen from <c>GET /branches/lookup</c>. Its code goes into the registration
    /// id and its name is stored on the student, matching the legacy column.
    /// Required: a student always belongs to a branch. Kept nullable so a request
    /// that omits it fails validation with "Please select a branch" rather than
    /// being silently model-bound to branch 0.
    /// </summary>
    public int? BranchId { get; init; }
    public string? PersonalEmail { get; init; }

    /// <summary>
    /// The Avijan portal sign-in. Leave both blank on create and the server
    /// generates them from the student's name, the way the legacy form did.
    /// </summary>
    public string? AvijanLoginEmail { get; init; }

    public string? AvijanLoginPassword { get; init; }
}

/// <summary>What the client gets back after a successful create.</summary>
public record StudentCreatedDto(int Id, string RegistrationId, string BillNo);

/// <summary>Next registration/bill number, for previewing on the form.</summary>
public record NextRegistrationDto(string RegistrationId, string BillNo);

/// <summary>Admission receipt (§4.1 S8) — replaces <c>Views/Home/PrintReport.cshtml</c>.</summary>
public record AdmissionReceiptDto(
    int Id,
    string? Name,
    string? RegistrationId,
    string? BillNo,
    string? CourseName,
    decimal? AdmissionFees,
    string AdmissionFeesInWords,
    DateOnly? DateOfJoining,
    /// <summary>DOJ + 32 days — the legacy <c>convert3()</c> next-due date.</summary>
    DateOnly? NextDueDate,

    /// <summary>
    /// Absolute URL of the PDF, for the WhatsApp message. A wa.me link can carry
    /// text and nothing else, so the receipt travels as a link the student taps
    /// rather than as an attachment. Signed and expiring - see IReceiptLinkService.
    /// </summary>
    string? PdfUrl = null);

/// <summary>Filters for the list endpoint.</summary>
public class StudentQuery : PagedQuery
{
    /// <summary>null = both, true = active only, false = inactive only. Defaults to active.</summary>
    public bool? IsActive { get; set; } = true;

    public int? CourseId { get; set; }
    public int? TeacherId { get; set; }

    /// <summary>Search restricted to the registration id (legacy <c>SearchData</c>).</summary>
    public string? RegistrationId { get; set; }

    /// <summary>Search restricted to the student name (legacy <c>SearchData1</c>).</summary>
    public string? Name { get; set; }
}

/// <summary>
/// A suggested portal sign-in for a student name, so the registration form can
/// show what will be issued before the student is saved.
/// </summary>
public record SuggestedLoginDto(string Email, string Password);

/* ============================================ admission fee, part payment */

/// <summary>One receipt taken against a student's admission fee.</summary>
public record AdmissionFeeReceiptDto(
    int Id,
    decimal Amount,
    string? PaymentMode,
    DateOnly PaidOn,
    /// <summary>True for the money taken at admission itself, false for a later instalment.</summary>
    bool IsAtAdmission,
    string? Note,
    string? CreatedBy);

/// <summary>
/// Where a student stands on their admission fee: what it is, what has come in,
/// and what is still owed.
/// </summary>
public record AdmissionDueDto(
    int StudentId,
    string? RegistrationId,
    string? StudentName,
    decimal AdmissionFees,
    decimal AdmissionFeesPaid,
    /// <summary>Never negative — a fee reduced below what was already taken reads as settled, not as a credit.</summary>
    decimal AdmissionDue,
    IReadOnlyList<AdmissionFeeReceiptDto> Receipts);

/// <summary>An instalment being taken against an outstanding admission fee.</summary>
public record AdmissionDueCollectRequest
{
    public decimal Amount { get; init; }

    /// <summary><c>Cash</c> or <c>Bank</c>.</summary>
    public string PaymentMode { get; init; } = "Cash";

    /// <summary>The day the money was taken. Defaults to today when not supplied.</summary>
    public DateOnly? PaidOn { get; init; }

    public string? Note { get; init; }
}
