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
    decimal? MonthlyFees,
    DateOnly? DateOfJoining,
    DateOnly? DateOfBirth,
    DateOnly? DateOfExit,
    bool IsActive,
    bool IsComplete,
    int? BranchId,
    string? Branch,
    bool HasPhoto);

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
    /// </summary>
    public int? BranchId { get; init; }
    public string? PersonalEmail { get; init; }
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
    DateOnly? NextDueDate);

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
