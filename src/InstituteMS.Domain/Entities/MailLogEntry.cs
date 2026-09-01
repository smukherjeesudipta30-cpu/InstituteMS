namespace InstituteMS.Domain.Entities;

/// <summary>
/// One attempt to send a registration email. Maps to
/// <c>dbo.MailLog_registration_time</c>, the table the legacy application used.
/// </summary>
/// <remarks>
/// Kept because a send that fails silently is worse than one that fails loudly:
/// production's 26 legacy attempts were all failures, and the only reason that is
/// knowable is this table.
/// </remarks>
public class MailLogEntry
{
    public int Id { get; set; }

    /// <summary>
    /// The institute-issued address the student signs in with. Named
    /// <c>For_registraion_id</c> in the legacy schema, misspelling and all.
    /// </summary>
    public string? ForRegistrationId { get; set; }

    /// <summary>Where it was sent - the student's personal address.</summary>
    public string ToEmail { get; set; } = string.Empty;

    public string? Subject { get; set; }
    public string? MailBody { get; set; }

    /// <summary><c>SENT</c>, <c>FAILED</c> or <c>SKIPPED</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
    public DateTime CreatedDate { get; set; }
    public string? CreatedBy { get; set; }
}
