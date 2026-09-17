using InstituteMS.Application.Common;

namespace InstituteMS.Application.Enquiries;

/// <summary>One row of the walk-in enquiry register.</summary>
public record CourseEnquiryDto(
    int Id,
    string StudentName,
    string Phone,
    string? Address,
    int? CourseId,
    string? CourseName,
    string? Remarks,
    DateOnly EnquiryDate,
    string Status,
    DateOnly? NextFollowUpOn,
    DateOnly? LastFollowUpOn,
    int FollowUpCount,
    int? BranchId,
    string? BranchName,
    string? ConvertedStudentRegistrationId,

    /// <summary>
    /// Open, and the follow-up date has arrived or passed. Worked out on the
    /// server so the list and the report cannot disagree about what "due" means.
    /// </summary>
    bool IsDue,

    string? CreatedBy,
    DateTime CreatedOn);

public record CourseEnquirySaveRequest
{
    public string StudentName { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string? Address { get; init; }

    /// <summary>Optional: people ask about things that are not in the catalogue.</summary>
    public int? CourseId { get; init; }

    public string? Remarks { get; init; }
    public DateOnly? EnquiryDate { get; init; }
    public DateOnly? NextFollowUpOn { get; init; }
    public int? BranchId { get; init; }
}

/// <summary>
/// Recording a follow-up call. Kept separate from the full save so a receptionist
/// on the phone updates only what the call changed, and cannot accidentally
/// overwrite the address while doing it.
/// </summary>
public record CourseEnquiryFollowUpRequest
{
    /// <summary>One of <c>CourseEnquiryStatus</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>What was said. Appended to the running remarks, never replacing them.</summary>
    public string? Remarks { get; init; }

    /// <summary>When to try again. Left empty when the enquiry is being closed.</summary>
    public DateOnly? NextFollowUpOn { get; init; }

    /// <summary>Set when the status becomes Converted.</summary>
    public string? ConvertedStudentRegistrationId { get; init; }
}

public class CourseEnquiryQuery : PagedQuery
{
    /// <summary>Name or phone number.</summary>
    public string? Search { get; set; }

    public string? Status { get; set; }
    public int? CourseId { get; set; }
    public int? BranchId { get; set; }

    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    /// <summary>Only those still open whose follow-up date has arrived.</summary>
    public bool DueOnly { get; set; }
}

/* ------------------------------------------------------------------ report */

/// <summary>
/// The follow-up report: the same rows the register shows, plus the totals the
/// office actually asks for — how many came in, how many turned into admissions,
/// and how many are still waiting on a call.
/// </summary>
public record CourseEnquiryReportDto(
    IReadOnlyList<CourseEnquiryDto> Rows,
    CourseEnquiryTotalsDto Totals,
    IReadOnlyList<CourseEnquiryCourseTotalDto> ByCourse,
    DateOnly? FromDate,
    DateOnly? ToDate);

public record CourseEnquiryTotalsDto(
    int Total,
    int New,
    int Following,
    int Converted,
    int NotInterested,

    /// <summary>Open and past their follow-up date — the ones being neglected.</summary>
    int Due,

    /// <summary>Converted as a percentage of everything in range, to one decimal.</summary>
    decimal ConversionRate);

public record CourseEnquiryCourseTotalDto(
    int? CourseId,
    string CourseName,
    int Enquiries,
    int Converted);
