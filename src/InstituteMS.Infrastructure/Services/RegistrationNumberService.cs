using System.Data;
using InstituteMS.Application.Students;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Generates <c>StuRegId</c> (<c>AVI/{n}/{yy}</c>) and <c>BillNo</c> (<c>S-A-{n}-{yy}</c>).
///
/// The legacy version (Models/Student.cs:108-146) read
/// <c>SELECT TOP 1 id FROM Student WHERE IsActive=1 ORDER BY id DESC</c>, added one,
/// and inserted — with no lock and no transaction. Two concurrent admissions got
/// the same number, and because the seed came from the newest *active* row it also
/// collided whenever the newest rows were inactive. That produced the 12 duplicate
/// registration ids now in the data (defect D4).
///
/// This version takes the number from MAX(id) over the whole table inside the
/// caller's transaction, holding a range lock so a concurrent insert cannot pick
/// the same value, and then verifies the candidate is genuinely free.
/// </summary>
public class RegistrationNumberService
{
    private readonly AppDbContext _db;

    public RegistrationNumberService(AppDbContext db) => _db = db;

    /// <summary>
    /// Reserves the next pair. Must be called inside an open transaction — the
    /// locks it takes are only meaningful until that transaction commits.
    /// </summary>
    /// <param name="branchCode">
    /// Short branch code from <c>dbo.Branch.BranchCode</c> (Rajpur → <c>Raj</c>).
    /// Null or empty produces the original branch-less format.
    /// </param>
    public async Task<NextRegistrationDto> ReserveAsync(string? branchCode, CancellationToken ct = default)
    {
        var year = DateTime.Today.ToString("yy");

        // The sequence is transactional, so two concurrent admissions can never draw
        // the same value. It is also independent of the identity column, which leaves
        // gaps whenever an insert rolls back (db/migrations/009).
        var next = await NextFromSequenceAsync(ct);

        // Defensive: the migrated data contains hand-entered ids that were never part
        // of any sequence, so step over anything already taken.
        var guard = 0;
        while (await IsNumberTakenAsync(next, year, ct))
        {
            next = await NextFromSequenceAsync(ct);

            if (++guard > 1000)
                throw new InvalidOperationException("Could not find a free registration number after 1000 attempts.");
        }

        return new NextRegistrationDto(Format(next, year, branchCode), FormatBill(next, year));
    }

    /// <summary>Read-only preview for the form. Not a reservation — the real number is taken on save.</summary>
    public async Task<NextRegistrationDto> PeekAsync(string? branchCode = null, CancellationToken ct = default)
    {
        // Reads the sequence's current position without consuming a value.
        var current = await _db.Database
            .SqlQuery<int?>(
                $"SELECT CONVERT(int, current_value) + 1 AS Value FROM sys.sequences WHERE name = 'StudentRegistrationNumber'")
            .FirstOrDefaultAsync(ct);

        var next = current ?? (await _db.Students.AsNoTracking().MaxAsync(s => (int?)s.Id, ct) ?? 0) + 1;
        var year = DateTime.Today.ToString("yy");

        return new NextRegistrationDto(Format(next, year, branchCode), FormatBill(next, year));
    }

    /// <summary>
    /// Draws the next value. This uses a raw command rather than
    /// <c>Database.SqlQuery</c> because EF wraps that in a derived table, and
    /// <c>NEXT VALUE FOR</c> is not allowed inside a subquery. The command is
    /// enlisted in the caller's transaction so the value is rolled back with it.
    /// </summary>
    private async Task<int> NextFromSequenceAsync(CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT NEXT VALUE FOR dbo.StudentRegistrationNumber";
        command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();

        var value = await command.ExecuteScalarAsync(ct);

        return Convert.ToInt32(value);
    }

    /// <summary>
    /// Checks the *number*, not the formatted string. <c>AVI/3463/26</c> and
    /// <c>AVI/Bar/3463/26</c> are different strings but the same registration number,
    /// so comparing whole strings would let it be issued twice once the branch
    /// segment was introduced.
    /// </summary>
    private Task<bool> IsNumberTakenAsync(int number, string year, CancellationToken ct)
    {
        var plain = $"AVI/{number}/{year}";
        var branchSuffix = $"/{number}/{year}";
        var bare = number.ToString();

        return _db.Students.AnyAsync(s =>
            s.RegistrationId == plain ||
            s.RegistrationId == bare ||
            (s.RegistrationId != null && s.RegistrationId.EndsWith(branchSuffix)), ct);
    }

    /// <summary>
    /// <c>AVI/{branch}/{n}/{yy}</c> when a branch is chosen, otherwise the original
    /// <c>AVI/{n}/{yy}</c> that 448 of the 459 migrated students use.
    ///
    /// The live system produced two broken variants of the branch form —
    /// <c>AVI//Raj/3455/2</c> (empty segment, year truncated to one digit) and
    /// <c>AVI/Raj/3461/26</c> for a student whose branch is actually Baruipur. Both
    /// are avoided here: the separator is built once, and the code comes from the
    /// student's own branch row rather than being fixed.
    /// </summary>
    private static string Format(int number, string year, string? branchCode)
    {
        var branch = branchCode?.Trim();

        return string.IsNullOrEmpty(branch)
            ? $"AVI/{number}/{year}"
            : $"AVI/{branch}/{number}/{year}";
    }

    private static string FormatBill(int number, string year) => $"S-A-{number}-{year}";
}
