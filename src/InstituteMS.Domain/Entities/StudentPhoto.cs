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

    /// <summary>
    /// Not actually persisted — <c>dbo.ImageUpload</c> was never given
    /// CreatedBy/CreatedOn columns, unlike the tables the coexistence script
    /// did add them to. Confirmed directly against the live table (8 columns,
    /// neither present); every upload was failing with "Invalid column name
    /// 'CreatedBy'" until <c>StudentPhotoConfiguration</c> started ignoring
    /// both. Left on the entity, unset, rather than removed outright, in case
    /// the table is ever given real audit columns later.
    /// </summary>
    public string? CreatedBy { get; set; }
    public DateTime? CreatedOn { get; set; }
}
