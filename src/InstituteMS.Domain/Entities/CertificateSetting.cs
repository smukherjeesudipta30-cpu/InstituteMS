namespace InstituteMS.Domain.Entities;

/// <summary>
/// Who signs the completion certificate, and the signature (and optional stamp)
/// printed on it — maps to <c>dbo.CertificateSetting</c>, a single row (Id 1).
/// The images are PNG/JPEG kept as Base64, uploaded by Admin.
/// </summary>
public class CertificateSetting
{
    public int Id { get; set; } = 1;
    public string SignatoryName { get; set; } = string.Empty;
    public string SignatoryTitle { get; set; } = "Branch-In-Charge";
    public string? SignatureContentType { get; set; }
    public string? SignatureImage { get; set; }
    public string? StampContentType { get; set; }
    public string? StampImage { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedOn { get; set; }
}
