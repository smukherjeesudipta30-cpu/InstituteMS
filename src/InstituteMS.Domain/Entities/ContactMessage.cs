namespace InstituteMS.Domain.Entities;

/// <summary>Maps to <c>dbo.CusMessage</c> — enquiries submitted from the public About Us page.</summary>
public class ContactMessage
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Body { get; set; }
    public DateTime SubmittedOn { get; set; }
    public string? SourceIp { get; set; }
    public bool IsHandled { get; set; }
}
