namespace InstituteMS.Domain.Entities;

/// <summary>
/// Maps to <c>dbo.ImageUpload</c>. <see cref="FilePath"/> holds a Base64 payload,
/// kept for compatibility with the legacy upload path (MIGRATION_PLAN §4.10 I1).
/// </summary>
public class StudentPhoto
{
    public int FileId { get; set; }
    public string? FileName { get; set; }
    /// <summary>Legacy column that doubled as the StuRegId join key.</summary>
    public string? Description { get; set; }
    /// <summary>Normalised replacement for the join-on-Description hack.</summary>
    public string? StudentRegistrationId { get; set; }
    /// <summary>
    /// Either a Base64 payload or a legacy disk path — <see cref="IsInline"/> says which.
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// True when <see cref="FilePath"/> is a Base64 payload this API can serve.
    /// False for rows written by the old disk-based uploader, whose bytes live on
    /// the web server rather than in the database.
    /// </summary>
    public bool IsInline { get; set; }
    public int? FileSize { get; set; }
    public string? ContentType { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
}
