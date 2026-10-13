using System.Security.Cryptography;
using System.Text;
using InstituteMS.Application.Certificates;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The certificate's signatory (name, title, signature and stamp images), and
/// the public check behind each certificate's QR code.
/// </summary>
public class CertificateService : ICertificateService
{
    private const int MaxImageBytes = 512 * 1024;

    /** What the certificate's artwork prints under the signature line. */
    public const string DefaultTitle = "Branch-In-Charge";
    private static readonly string[] ImageTypes = ["image/png", "image/jpeg"];

    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;

    public CertificateService(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<CertificateSettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var s = await _db.CertificateSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
        return s is null
            ? new CertificateSettingsDto(string.Empty, DefaultTitle, null, null, null, null)
            : new CertificateSettingsDto(s.SignatoryName, s.SignatoryTitle,
                DataUrl(s.SignatureContentType, s.SignatureImage), DataUrl(s.StampContentType, s.StampImage),
                s.ModifiedBy, s.ModifiedOn);
    }

    public async Task<Result<CertificateSettingsDto>> SaveSettingsAsync(
        CertificateSettingsSaveRequest request, string? actor, CancellationToken ct = default)
    {
        // The name is the office's record of who signs; the certificate itself
        // reads "Branch-In-Charge", printed in its artwork.
        if ((request.SignatoryName ?? string.Empty).Length > 100)
            return Result<CertificateSettingsDto>.Failure("The name is too long.");

        var row = await _db.CertificateSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (row is null)
        {
            row = new CertificateSetting { Id = 1 };
            _db.CertificateSettings.Add(row);
        }

        row.SignatoryName = (request.SignatoryName ?? string.Empty).Trim();
        row.SignatoryTitle = DefaultTitle;

        if (request.RemoveSignature) { row.SignatureImage = null; row.SignatureContentType = null; }
        else if (!string.IsNullOrWhiteSpace(request.SignatureBase64))
        {
            var check = CheckImage(request.SignatureContentType, request.SignatureBase64, "signature");
            if (check is not null) return Result<CertificateSettingsDto>.Failure(check);
            row.SignatureContentType = request.SignatureContentType;
            row.SignatureImage = Strip(request.SignatureBase64);
        }

        if (request.RemoveStamp) { row.StampImage = null; row.StampContentType = null; }
        else if (!string.IsNullOrWhiteSpace(request.StampBase64))
        {
            var check = CheckImage(request.StampContentType, request.StampBase64, "stamp");
            if (check is not null) return Result<CertificateSettingsDto>.Failure(check);
            row.StampContentType = request.StampContentType;
            row.StampImage = Strip(request.StampBase64);
        }

        row.ModifiedBy = actor;
        row.ModifiedOn = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return Result<CertificateSettingsDto>.Success(await GetSettingsAsync(ct));
    }

    public async Task<Result<CertificateVerificationDto>> VerifyAsync(string code, CancellationToken ct = default)
    {
        var issueId = CertificateCodes.IssueIdFrom(code, SigningKey(_configuration));
        if (issueId is null)
            return Result<CertificateVerificationDto>.Failure("This certificate code is not valid.", ResultStatus.NotFound);

        var issue = await _db.CertificateIssues.AsNoTracking().FirstOrDefaultAsync(i => i.IssueId == issueId, ct);
        if (issue is null)
            return Result<CertificateVerificationDto>.Failure("This certificate code is not valid.", ResultStatus.NotFound);

        var student = await _db.Students.AsNoTracking()
            .Where(s => s.RegistrationId == issue.StudentRegistrationId)
            .Select(s => new { s.Name, s.RegistrationId, CourseName = s.Course != null ? s.Course.Name : null })
            .FirstOrDefaultAsync(ct);

        return Result<CertificateVerificationDto>.Success(new CertificateVerificationDto(
            CertificateCodes.Number(issue.IssueId), student?.Name, student?.RegistrationId ?? issue.StudentRegistrationId,
            student?.CourseName, issue.Grade, issue.CompletedOn, issue.IssueNo, issue.IssueNo > 1, issue.IssuedOn));
    }

    /// <summary>The key the codes are signed with — the server's own JWT key, never sent anywhere.</summary>
    internal static string SigningKey(IConfiguration configuration) =>
        configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");

    private static string? DataUrl(string? type, string? base64) =>
        string.IsNullOrEmpty(base64) ? null : $"data:{type ?? "image/png"};base64,{base64}";

    private static string Strip(string base64)
    {
        var comma = base64.IndexOf(',');
        return base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0 ? base64[(comma + 1)..] : base64;
    }

    private static string? CheckImage(string? type, string base64, string what)
    {
        if (!ImageTypes.Contains(type ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            return $"The {what} must be a PNG or JPEG picture.";
        try
        {
            var bytes = Convert.FromBase64String(Strip(base64));
            if (bytes.Length == 0) return $"The {what} picture is empty.";
            if (bytes.Length > MaxImageBytes) return $"The {what} picture is too large — please use one under 512 KB.";
        }
        catch (FormatException)
        {
            return $"The {what} picture could not be read.";
        }
        return null;
    }
}

/// <summary>
/// A certificate's number ("AVI-C-000123") and the code its QR carries
/// ("123-9f3a1c07e2"): the issue id plus a keyed hash of it, so a code cannot be
/// guessed or made up for an id that was never issued.
/// </summary>
public static class CertificateCodes
{
    public static string Number(int issueId) => $"AVI-C-{issueId:000000}";

    public static string For(int issueId, string key) => $"{issueId}-{Mac(issueId, key)}";

    public static int? IssueIdFrom(string? code, string key)
    {
        var parts = (code ?? string.Empty).Trim().Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var id) || id <= 0) return null;
        var expected = Mac(id, key);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[1].ToLowerInvariant()))
            ? id
            : null;
    }

    private static string Mac(int issueId, string key)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes($"certificate:{issueId}"));
        return Convert.ToHexString(hash, 0, 5).ToLowerInvariant();
    }
}
