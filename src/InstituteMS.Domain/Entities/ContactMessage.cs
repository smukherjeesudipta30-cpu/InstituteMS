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
    /// <summary>
    /// Not persisted - dbo.CusMessage cannot gain a column. The real flag is
    /// <see cref="ContactMessageExt.IsHandled"/>, reached through <see cref="Ext"/>.
    /// </summary>
    public bool IsHandled { get; set; }

    /// <summary>Side row carrying the handled flag. Null means not handled.</summary>
    public ContactMessageExt? Ext { get; set; }
}
