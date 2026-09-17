namespace InstituteMS.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.CertificateIssue</c> — one row per certificate handed out.
/// </summary>
/// <remarks>
/// The institute vouches for a certificate, so the first copy a student takes is
/// the original and every one after it is a duplicate, marked as such on its face
/// with its own copy number. An unmarked second copy is indistinguishable from the
/// first, which is precisely what the marking prevents.
/// </remarks>
public class CertificateIssue
{
    public int IssueId { get; set; }

    public string StudentRegistrationId { get; set; } = string.Empty;

    /// <summary>1 is the original; 2 and above print as duplicates.</summary>
    public int IssueNo { get; set; }

    public DateTime IssuedOn { get; set; }

    /// <summary>The sign-in that took it, so the office can say who and not just when.</summary>
    public string? IssuedTo { get; set; }
}
