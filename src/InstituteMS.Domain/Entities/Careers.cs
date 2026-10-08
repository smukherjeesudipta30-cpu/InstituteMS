namespace InstituteMS.Domain.Entities;

/// <summary>
/// A job opening Admin advertises on the public site — maps to
/// <c>dbo.JobVacancy</c>. Receptionist posts only for now, one branch each.
/// </summary>
public class JobVacancy
{
    public int Id { get; set; }

    /// <summary>The post — "Receptionist".</summary>
    public string Title { get; set; } = "Receptionist";

    /// <summary>Branch name, as on the Branches screen.</summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>The shift's name as shown to applicants — "Morning", "Evening".</summary>
    public string? ShiftName { get; set; }
    public TimeOnly? ShiftStart { get; set; }
    public TimeOnly? ShiftEnd { get; set; }

    /// <summary>"Monday to Saturday" and the like.</summary>
    public string? WorkingDays { get; set; }

    /// <summary>The academic criteria — "Graduate (any stream)".</summary>
    public string Qualification { get; set; } = string.Empty;

    public string? Experience { get; set; }
    public string? Salary { get; set; }
    public string? Description { get; set; }

    public int Openings { get; set; } = 1;

    /// <summary>The last day applications are taken. Null keeps it open until Admin closes it.</summary>
    public DateOnly? ClosesOn { get; set; }

    /// <summary>Shown on the public site only while true.</summary>
    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}

/// <summary>
/// Someone applying for a <see cref="JobVacancy"/> from the public site — maps
/// to <c>dbo.JobApplication</c>. The CV and photograph are kept on the row, the
/// same way competition documents are.
/// </summary>
public class JobApplication
{
    public int Id { get; set; }
    public int VacancyId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Qualification { get; set; } = string.Empty;
    public string? Experience { get; set; }
    public string? Address { get; set; }
    public string? Message { get; set; }

    public string? CvFileName { get; set; }
    public string? CvContentType { get; set; }
    public string CvContent { get; set; } = string.Empty;

    public string? PhotoContentType { get; set; }
    public string PhotoContent { get; set; } = string.Empty;

    /// <summary><see cref="JobApplicationStatuses"/>.</summary>
    public string Status { get; set; } = JobApplicationStatuses.New;
    public string? AdminNote { get; set; }

    public DateTime AppliedOn { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedOn { get; set; }

    public JobVacancy? Vacancy { get; set; }
}

public static class JobApplicationStatuses
{
    public const string New = "New";
    public const string Shortlisted = "Shortlisted";
    public const string Interviewed = "Interviewed";
    public const string Hired = "Hired";
    public const string Rejected = "Rejected";

    public static readonly string[] All = [New, Shortlisted, Interviewed, Hired, Rejected];
}
