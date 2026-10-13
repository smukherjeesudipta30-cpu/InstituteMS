using InstituteMS.Application.Common;

namespace InstituteMS.Application.Certificates;

/// <summary>The signatory and images Admin set for the certificate. Images are data: URLs.</summary>
public record CertificateSettingsDto(
    string SignatoryName,
    string SignatoryTitle,
    string? SignatureDataUrl,
    string? StampDataUrl,
    string? ModifiedBy,
    DateTime? ModifiedOn);

/// <summary>
/// Saves the signatory. An image left null is kept as it is; send
/// <see cref="RemoveSignature"/>/<see cref="RemoveStamp"/> to clear one.
/// </summary>
public record CertificateSettingsSaveRequest(
    string SignatoryName,
    string SignatoryTitle,
    string? SignatureContentType,
    string? SignatureBase64,
    string? StampContentType,
    string? StampBase64,
    bool RemoveSignature = false,
    bool RemoveStamp = false);

/// <summary>What the public verify page shows for a certificate's QR code.</summary>
public record CertificateVerificationDto(
    string CertificateNo,
    string? StudentName,
    string? RegistrationId,
    string? CourseName,
    string? Grade,
    DateOnly? CompletedOn,
    int IssueNo,
    bool IsDuplicate,
    DateTime IssuedOn);

public interface ICertificateService
{
    Task<CertificateSettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<Result<CertificateSettingsDto>> SaveSettingsAsync(CertificateSettingsSaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>Checks a certificate's QR code. Unknown or forged codes are NotFound.</summary>
    Task<Result<CertificateVerificationDto>> VerifyAsync(string code, CancellationToken ct = default);
}
