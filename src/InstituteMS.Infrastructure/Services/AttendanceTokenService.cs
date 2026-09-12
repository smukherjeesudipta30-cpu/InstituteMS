using System.Security.Cryptography;
using System.Text;
using InstituteMS.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The short-lived code inside the attendance QR: <c>{branchId}.{expiry}.{signature}</c>,
/// base64url.
/// </summary>
/// <remarks>
/// <para>The QR on the centre's screen is a photograph waiting to happen — someone
/// snaps it and sends it to a colleague who is still at home. Two things stop that
/// working. The phone must be inside the branch's fence, which is checked when the
/// scan arrives; and the code itself dies after ninety seconds, so a forwarded
/// picture is stale long before it is useful.</para>
///
/// <para>Signed with the JWT key for the same reasons as
/// <see cref="Documents.ReceiptLinkService"/>: it is already a required secret of
/// adequate length, and rotating it invalidates outstanding codes, which is the
/// wanted behaviour. Comparison is fixed-time so a signature cannot be guessed a
/// byte at a time from how long the check takes.</para>
/// </remarks>
public class AttendanceTokenService
{
    private readonly byte[] _key;
    private readonly AttendanceOptions _options;

    public AttendanceTokenService(IOptions<JwtSettings> jwt, IOptions<AttendanceOptions> options)
    {
        _options = options.Value;

        var key = jwt.Value.Key;

        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key must be set and at least 32 characters; attendance codes are signed with it.");
        }

        _key = Encoding.UTF8.GetBytes(key);
    }

    public int TokenSeconds => Math.Max(15, _options.TokenSeconds);

    /// <summary>Mints a code for one branch, good for <see cref="TokenSeconds"/>.</summary>
    public (string Token, DateTime ExpiresOn) Create(int branchId)
    {
        var expiresOn = DateTimeOffset.UtcNow.AddSeconds(TokenSeconds);
        var payload = $"{branchId}.{expiresOn.ToUnixTimeSeconds()}";

        var token = Base64Url($"{payload}.{Sign(payload)}");

        return (token, expiresOn.LocalDateTime);
    }

    /// <summary>
    /// Reads a scanned code back. Returns null for anything malformed, tampered
    /// with or out of date — the caller says only "scan again", because telling a
    /// forger which of the three it was is telling them how to improve.
    /// </summary>
    public int? ReadBranchId(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        string decoded;

        try
        {
            var padded = token.Trim().Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        }
        catch (FormatException)
        {
            return null;
        }

        var parts = decoded.Split('.');
        if (parts.Length != 3) return null;

        if (!int.TryParse(parts[0], out var branchId)) return null;
        if (!long.TryParse(parts[1], out var expiry)) return null;

        var payload = $"{parts[0]}.{parts[1]}";
        if (!FixedTimeEquals(Sign(payload), parts[2])) return null;

        // Checked after the signature, so an expired-but-valid code and a forged one
        // take the same path and the same time.
        return DateTimeOffset.FromUnixTimeSeconds(expiry) < DateTimeOffset.UtcNow
            ? null
            : branchId;
    }

    private string Sign(string payload)
    {
        using var hmac = new HMACSHA256(_key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var left = Encoding.UTF8.GetBytes(a);
        var right = Encoding.UTF8.GetBytes(b);

        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
