using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace InstituteMS.Infrastructure.Security;

/// <summary>
/// Settings for the readable copy of a student's portal password, bound from the
/// <c>StudentPasswords</c> section of appsettings.json.
/// </summary>
public class StudentPasswordOptions
{
    public const string SectionName = "StudentPasswords";

    /// <summary>
    /// A 32-byte AES key, base64. Empty (the default) leaves the column in clear,
    /// exactly as before.
    /// </summary>
    /// <remarks>
    /// Off by default because the legacy ASP.NET MVC application shares this
    /// database and may still read <c>Avijan_login_Password</c> in clear; it
    /// would see ciphertext once this is on. Set it only once that application
    /// is retired. A credential, same footing as Jwt.Key: real value only in the
    /// untracked appsettings.Production.json, and never changed afterwards — a
    /// lost or changed key makes every encrypted copy unreadable (sign-in is
    /// unaffected; it checks <c>Users.PasswordHash</c>).
    /// </remarks>
    public string EncryptionKey { get; set; } = string.Empty;
}

/// <summary>
/// AES-GCM for the readable copy of a student's portal password on
/// <c>Student.Avijan_login_Password</c> — kept so the office can tell a student
/// their password, so it cannot be a hash like <c>Users.PasswordHash</c>.
/// </summary>
/// <remarks>
/// <para>
/// Static rather than injected because it is consumed by an EF value converter,
/// which the model builds once, from configuration classes EF instantiates
/// itself. <see cref="Configure"/> runs in AddInfrastructure, before any
/// context exists.
/// </para>
/// <para>
/// Values carry a <see cref="Prefix"/>, so clear legacy values and encrypted ones
/// can sit side by side: a clear value reads back as itself, and is encrypted
/// the next time it is written (or by <see cref="EncryptStoredAsync"/>).
/// </para>
/// </remarks>
public static class StudentPasswordCipher
{
    public const string Prefix = "enc:v1:";

    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    /// <summary>The column's width; an encrypted value must fit it.</summary>
    private const int ColumnLength = 250;

    private static byte[]? _key;

    public static bool Enabled => _key is not null;

    public static void Configure(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            _key = null;
            return;
        }

        var key = Convert.FromBase64String(base64Key.Trim());
        if (key.Length != 32)
            throw new InvalidOperationException(
                $"{StudentPasswordOptions.SectionName}:EncryptionKey must be 32 bytes, base64.");
        _key = key;
    }

    public static string? Protect(string? value)
    {
        if (_key is null || string.IsNullOrEmpty(value) || value.StartsWith(Prefix, StringComparison.Ordinal))
            return value;

        var plain = Encoding.UTF8.GetBytes(value);
        var blob = new byte[NonceBytes + TagBytes + plain.Length];
        var nonce = blob.AsSpan(0, NonceBytes);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, plain, blob.AsSpan(NonceBytes + TagBytes), blob.AsSpan(NonceBytes, TagBytes));

        var stored = Prefix + Convert.ToBase64String(blob);
        if (stored.Length > ColumnLength)
            throw new InvalidOperationException("The password is too long to store.");
        return stored;
    }

    /// <summary>
    /// The clear password. A clear legacy value comes back as itself; an
    /// encrypted one that cannot be read (no key, or a different key) comes back
    /// as null rather than as ciphertext shown to the office as a password.
    /// </summary>
    public static string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return value;
        if (_key is null)
            return null;

        try
        {
            var blob = Convert.FromBase64String(value[Prefix.Length..]);
            var plain = new byte[blob.Length - NonceBytes - TagBytes];

            using var aes = new AesGcm(_key, TagBytes);
            aes.Decrypt(blob.AsSpan(0, NonceBytes), blob.AsSpan(NonceBytes + TagBytes),
                blob.AsSpan(NonceBytes, TagBytes), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e) when (e is FormatException or CryptographicException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Encrypts every copy still in clear. Idempotent, and does nothing while the
    /// cipher is off. Raw SQL on both sides because the converter would hide the
    /// prefix from a LINQ filter.
    /// </summary>
    public static async Task<int> EncryptStoredAsync(Persistence.AppDbContext db, CancellationToken ct = default)
    {
        if (!Enabled) return 0;

        var rows = await db.Database
            .SqlQueryRaw<ClearRow>(
                "SELECT Id, Avijan_login_Password AS Password FROM dbo.Student " +
                "WHERE Avijan_login_Password IS NOT NULL AND Avijan_login_Password <> '' " +
                "AND Avijan_login_Password NOT LIKE 'enc:v1:%'")
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.Student SET Avijan_login_Password = {Protect(row.Password)} WHERE Id = {row.Id}", ct);
        }
        return rows.Count;
    }

    private sealed class ClearRow
    {
        public int Id { get; set; }
        public string Password { get; set; } = string.Empty;
    }
}

/// <summary>The EF side of <see cref="StudentPasswordCipher"/>.</summary>
public class StudentPasswordConverter : ValueConverter<string?, string?>
{
    public StudentPasswordConverter()
        : base(v => StudentPasswordCipher.Protect(v), v => StudentPasswordCipher.Unprotect(v))
    {
    }
}
