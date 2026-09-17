using System.Text;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Builds the Avijan portal sign-in a student is given when they are registered.
///
/// <para>
/// The rule is taken from the live data rather than from code — the generation was
/// done in the browser and is not in the 31-05-2026 source backup. Every one of the
/// 30 generated logins on production follows the same shape:
/// </para>
///
/// <code>
/// ANKITA BHUNIA -> ankitabhunia@avijan.in / ankitabhunia@1234
/// SAYAN NANDI   -> sayannandi@avijan.in   / sayannandi@1234
/// </code>
///
/// <para>
/// So: the name lower-cased with spaces removed, then <c>@avijan.in</c> for the
/// address and <c>@1234</c> for the password. The password's first letter is
/// capitalised (<c>Ankitabhunia@1234</c>) at the owner's request — a password
/// that is otherwise indistinguishable from the lower-cased email address read
/// as an easy typo to make when copying one to the other, and a leading
/// capital gives the two a visible difference at a glance. The email's own
/// case is untouched: it is what production already issued for 30 real
/// students, and changing it would silently break their existing sign-in.
/// </para>
/// </summary>
public class StudentLoginGenerator
{
    private const string Domain = "avijan.in";
    private const string PasswordSuffix = "@1234";

    private readonly AppDbContext _db;

    public StudentLoginGenerator(AppDbContext db) => _db = db;

    /// <summary>
    /// The email and password for a student name, with a numeric suffix added to
    /// the local part if that address is already taken.
    /// </summary>
    /// <remarks>
    /// Production has no duplicate generated address today, but two students called
    /// the same thing is only a matter of time — and the legacy would have created a
    /// second Users row on the same email, leaving sign-in to resolve between them.
    /// The suffix keeps each student's login distinct.
    /// </remarks>
    public async Task<(string Email, string Password)> GenerateAsync(
        string? studentName, int? excludingStudentId = null, CancellationToken ct = default)
    {
        var local = Localise(studentName);

        // Nothing usable in the name (blank, or punctuation only): fall back to a
        // neutral stem so a login can still be issued.
        if (local.Length == 0)
            local = "student";

        var candidate = local;

        for (var attempt = 2; await IsTakenAsync(candidate, excludingStudentId, ct); attempt++)
            candidate = $"{local}{attempt}";

        return ($"{candidate}@{Domain}", $"{Capitalise(candidate)}{PasswordSuffix}");
    }

    /// <summary>First character upper-cased, the rest left as is. Password only — never the email.</summary>
    private static string Capitalise(string value) =>
        value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    /// <summary>Lower-cased, spaces removed, and anything not a letter or digit dropped.</summary>
    private static string Localise(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var builder = new StringBuilder(name.Length);

        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        // The column is varchar(150) and the address adds the domain on top.
        return builder.Length > 100 ? builder.ToString(0, 100) : builder.ToString();
    }

    /// <summary>
    /// Taken if any other student already carries the address, or any user account
    /// does. Both are checked because the two are created together.
    /// </summary>
    private async Task<bool> IsTakenAsync(string local, int? excludingStudentId, CancellationToken ct)
    {
        var email = $"{local}@{Domain}";

        var onStudent = await _db.Students
            .AsNoTracking()
            .AnyAsync(s => s.AvijanLoginEmail == email
                           && (excludingStudentId == null || s.Id != excludingStudentId), ct);

        if (onStudent)
            return true;

        return await _db.Users.AsNoTracking().AnyAsync(u => u.Email == email, ct);
    }
}
