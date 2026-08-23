namespace InstituteMS.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.Student</c>. C# names are readable; the column names stay
/// exactly as the legacy schema has them (configured in StudentConfiguration).
/// </summary>
public class Student
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? GuardianName { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    /// <summary>Business key, format <c>AVI/{n}/{yy}</c>.</summary>
    public string? RegistrationId { get; set; }
    /// <summary>Admission bill number, format <c>S-A-{n}-{yy}</c>.</summary>
    public string? BillNo { get; set; }
    public int? TechniqueCount { get; set; }
    public decimal? AdmissionFees { get; set; }
    public decimal? MonthlyFees { get; set; }
    public DateOnly? DateOfJoining { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public DateOnly? DateOfExit { get; set; }
    public int? CourseId { get; set; }
    public int? PrimaryTeacherId { get; set; }
    public int? SecondTeacherId { get; set; }
    public int? ThirdTeacherId { get; set; }
    public bool IsActive { get; set; }
    public bool IsComplete { get; set; }
    /// <summary>User who created/last touched the row (legacy <c>patCode</c> = Session["UserID"]).</summary>
    public string? LoginId { get; set; }
    public string? Branch { get; set; }
    public string? PersonalEmail { get; set; }
    public string? AvijanLoginEmail { get; set; }
    /// <summary>Legacy self-reference written by <c>usp_StudentSaveData</c> (Replica_id = own id).</summary>
    public int? ReplicaId { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public Course? Course { get; set; }
    public Teacher? PrimaryTeacher { get; set; }
    public Teacher? SecondTeacher { get; set; }
    public Teacher? ThirdTeacher { get; set; }
}
