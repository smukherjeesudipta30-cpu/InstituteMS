namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.Course</c>.</summary>
public class Course
{
    public int Cid { get; set; }
    public string? Name { get; set; }
    /// <summary>Course length in months.</summary>
    public int? Duration { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }

    public ICollection<Student> Students { get; set; } = new List<Student>();
}
