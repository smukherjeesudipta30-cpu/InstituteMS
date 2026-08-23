namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.Teacher</c>.</summary>
public class Teacher
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? LoginId { get; set; }
    public string? Phone { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
