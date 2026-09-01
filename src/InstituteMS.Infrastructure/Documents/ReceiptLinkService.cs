using System.Security.Cryptography;
using System.Text;
using InstituteMS.Application.Documents;
using InstituteMS.Application.Notifications;
using InstituteMS.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace InstituteMS.Infrastructure.Documents;

/// <summary>
/// Receipt download tokens: <c>{kind}.{id}.{expiry}.{signature}</c>, base64url.
/// </summary>
/// <remarks>
/// <para>Keyed off the JWT signing key rather than a key of its own. That key is
/// already required to be at least 32 characters, is already treated as a secret,
/// and rotating it invalidates outstanding links — which is the behaviour wanted
/// anyway, since rotating it also signs everyone out.</para>
///
/// <para>The signature covers the kind, the id and the expiry together, so none of
/// them can be changed on their own. Comparison is fixed-time: comparing signatures
/// with ordinary string equality leaks, through timing, how much of a guess was
/// correct, which is enough to forge one a byte at a time.</para>
/// </remarks>
public class ReceiptLinkService : IReceiptLinkService
{
    /// <summary>
    /// How long a link stays good for. Long enough that a student can come back to
    /// an old message and still get their receipt, short enough that a link leaked
    /// years later is dead.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    private readonly byte[] _key;
    private readonly string _baseUrl;

    public ReceiptLinkService(IOptions<JwtSettings> jwt, IOptions<MailOptions> mail)
    {
        /* SiteBaseUrl lives under Mail because that is where it was first needed -
           the logo in an email also has to be an absolute URL. It means "where this
           site is", and there is no sense configuring it twice. */
        _baseUrl = mail.Value.SiteBaseUrl.TrimEnd('/');

        var key = jwt.Value.Key;

        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Key must be set and at least 32 characters; receipt links are signed with it.");
        }

        _key = Encoding.UTF8.GetBytes(key);
    }

    public string CreateToken(ReceiptKind kind, int id)
    {
        var expiry = DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds();
        var payload = $"{(int)kind}.{id}.{expiry}";

        return Base64Url($"{payload}.{Sign(payload)}");
    }

    public string CreateUrl(ReceiptKind kind, int id) =>
        $"{_baseUrl}/r/{CreateToken(kind, id)}";

    public (ReceiptKind Kind, int Id)? Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        string decoded;
        try { decoded = FromBase64Url(token); }
        catch { return null; }

        var parts = decoded.Split('.');
        if (parts.Length != 4) return null;

        var payload = $"{parts[0]}.{parts[1]}.{parts[2]}";

        // Fixed-time: an ordinary == would leak the signature a byte at a time.
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(Sign(payload)),
                Encoding.UTF8.GetBytes(parts[3])))
        {
            return null;
        }

        if (!int.TryParse(parts[0], out var kind) ||
            !int.TryParse(parts[1], out var id) ||
            !long.TryParse(parts[2], out var expiry))
        {
            return null;
        }

        if (DateTimeOffset.FromUnixTimeSeconds(expiry) < DateTimeOffset.UtcNow) return null;
        if (!Enum.IsDefined(typeof(ReceiptKind), kind)) return null;

        return ((ReceiptKind)kind, id);
    }

    private string Sign(string payload)
    {
        using var hmac = new HMACSHA256(_key);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    /* Base64url: the token goes in a URL, where +, / and = are not safe. */

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);

        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
