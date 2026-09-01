namespace InstituteMS.Domain.Entities;

/// <summary>
/// A course enquiry taken at the counter. Maps to <c>dbo.CourseEnquiry</c>.
/// </summary>
/// <remarks>
/// Distinct from <see cref="ContactMessage"/>, which is the public website's
/// "Send Us Message" form. This one is typed by a receptionist while the person is
/// standing there, and exists to be followed up.
/// </remarks>
public class CourseEnquiry
{
    public int Id { get; set; }

    public string StudentName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Address { get; set; }

    /// <summary>
    /// The course asked about. Nullable, and no foreign key: people ask vaguely,
    /// and removing a course must not remove the enquiries about it.
    /// </summary>
    public int? CourseId { get; set; }
    public Course? Course { get; set; }

    /// <summary>Anything that did not fit a column, and what was said on each call.</summary>
    public string? Remarks { get; set; }

    public DateOnly EnquiryDate { get; set; }

    /// <summary>One of <see cref="CourseEnquiryStatus"/>.</summary>
    public string Status { get; set; } = CourseEnquiryStatus.New;

    /// <summary>When to ring them next. The follow-up report is built around this.</summary>
    public DateOnly? NextFollowUpOn { get; set; }

    public DateOnly? LastFollowUpOn { get; set; }
    public int FollowUpCount { get; set; }

    public int? BranchId { get; set; }

    /// <summary>Set when the enquiry becomes an admission.</summary>
    public string? ConvertedStudentRegistrationId { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    /// <summary>Neither converted nor written off, so still worth a call.</summary>
    public bool IsOpen =>
        Status is CourseEnquiryStatus.New or CourseEnquiryStatus.Following;
}

/// <summary>
/// Where an enquiry has got to. Four values, deliberately: the office reads these
/// straight off a report, and a longer list would be guessed at rather than used.
/// </summary>
public static class CourseEnquiryStatus
{
    /// <summary>Taken at the counter, nobody has called back yet.</summary>
    public const string New = "New";

    /// <summary>Being chased.</summary>
    public const string Following = "Following";

    /// <summary>Became an admission.</summary>
    public const string Converted = "Converted";

    /// <summary>Said no, or went quiet. Closed either way.</summary>
    public const string NotInterested = "NotInterested";

    public static readonly string[] All = [New, Following, Converted, NotInterested];

    /// <summary>The two that still want a phone call.</summary>
    public static readonly string[] Open = [New, Following];

    public static bool IsKnown(string? status) =>
        status is not null && All.Contains(status.Trim(), StringComparer.OrdinalIgnoreCase);
}
