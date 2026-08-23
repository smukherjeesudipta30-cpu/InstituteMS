namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.Notice</c> — the notice board shown on the dashboard.</summary>
public class Notice
{
    public int Id { get; set; }
    public string? Text { get; set; }
    public DateTime? NoticeDate { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
