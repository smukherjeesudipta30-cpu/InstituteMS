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

/// <summary>
/// A row of the weekly class routine shown on the public front page
/// (legacy <c>dailyRoutine</c>, read by <c>Notice/gettime</c>).
/// </summary>
public class DailyRoutine
{
    public int Id { get; set; }

    /// <summary>Batch or class name the row is for.</summary>
    public string? Name { get; set; }

    public string? Sunday { get; set; }
    public string? Monday { get; set; }
    public string? Tuesday { get; set; }
    public string? Wednesday { get; set; }
    public string? Thursday { get; set; }
    public string? Friday { get; set; }
    public string? Saturday { get; set; }
}
