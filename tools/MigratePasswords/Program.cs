using Microsoft.Data.SqlClient;

/*  MigratePasswords
    ----------------
    Run once per environment, after db/migrations/026_force_password_reset.sql.

    The 756 accounts carried over from the legacy system arrived with their
    password in plain text in dbo.Users.Password and nothing in PasswordHash, so
    none of them could sign in. For each of those accounts this tool:

        1. BCrypts the legacy password into PasswordHash, at the same work factor
           the application uses, so the password the user already knows works;
        2. sets MustChangePassword, so the first thing they are made to do is
           choose a new one;
        3. blanks dbo.Users.Password, so the plain text is gone from the database
           the moment the hash exists rather than whenever the user gets round to
           signing in.

    Accounts that already have a hash are left alone - including the local
    administrator, who would otherwise be locked behind a reset they do not need.

    Usage:
        dotnet run --project tools/MigratePasswords -- "<connection string>" [--apply]

    Without --apply it reports what it would do and changes nothing.
*/

const int WorkFactor = 12;   // must match BCryptPasswordHasher

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: MigratePasswords \"<connection string>\" [--apply]");
    return 2;
}

var connectionString = args[0];
var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);

Console.WriteLine(apply
    ? "Mode: APPLY - the database will be modified."
    : "Mode: DRY RUN - nothing will be written. Pass --apply to commit.");
Console.WriteLine();

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

// Guard: the column the whole flow hangs on must exist before anything is touched.
await using (var check = new SqlCommand(
    "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'MustChangePassword';",
    connection))
{
    if ((int)(await check.ExecuteScalarAsync())! == 0)
    {
        Console.Error.WriteLine("dbo.Users.MustChangePassword is missing.");
        Console.Error.WriteLine("Run db/migrations/026_force_password_reset.sql first.");
        return 3;
    }
}

var pending = new List<(int Id, string Email, string Password)>();

await using (var read = new SqlCommand(@"
    SELECT id, ISNULL(Email, ''), Password
    FROM dbo.Users
    WHERE (PasswordHash IS NULL OR LEN(PasswordHash) = 0)
      AND Password IS NOT NULL
      AND LEN(LTRIM(RTRIM(Password))) > 0
    ORDER BY id;", connection))
await using (var reader = await read.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
        pending.Add((reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
}

Console.WriteLine($"Accounts to migrate: {pending.Count}");

if (pending.Count == 0)
{
    Console.WriteLine("Nothing to do.");
    return 0;
}

if (!apply)
{
    foreach (var (id, email, _) in pending.Take(10))
        Console.WriteLine($"  would migrate #{id}  {email}");

    if (pending.Count > 10)
        Console.WriteLine($"  ... and {pending.Count - 10} more");

    Console.WriteLine();
    Console.WriteLine("Dry run complete. Re-run with --apply to commit.");
    return 0;
}

var done = 0;
var failed = 0;
var started = DateTime.UtcNow;

foreach (var (id, email, legacy) in pending)
{
    try
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(legacy, WorkFactor);

        await using var update = new SqlCommand(@"
            UPDATE dbo.Users
            SET PasswordHash       = @hash,
                MustChangePassword = 1,
                Password           = '',
                ModifiedBy         = 'password-migration',
                ModifiedOn         = SYSUTCDATETIME()
            WHERE id = @id
              AND (PasswordHash IS NULL OR LEN(PasswordHash) = 0);", connection);

        update.Parameters.AddWithValue("@hash", hash);
        update.Parameters.AddWithValue("@id", id);

        if (await update.ExecuteNonQueryAsync() == 1)
            done++;
        else
            Console.WriteLine($"  skipped #{id} {email} - a hash appeared while running");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"  FAILED #{id} {email}: {ex.Message}");
    }

    if (done % 100 == 0 && done > 0)
        Console.WriteLine($"  {done}/{pending.Count} ...");
}

Console.WriteLine();
Console.WriteLine($"Migrated : {done}");
Console.WriteLine($"Failed   : {failed}");
Console.WriteLine($"Elapsed  : {(DateTime.UtcNow - started).TotalSeconds:F0}s");

await using (var verify = new SqlCommand(@"
    SELECT
        (SELECT COUNT(*) FROM dbo.Users WHERE PasswordHash IS NOT NULL AND LEN(PasswordHash) > 0),
        (SELECT COUNT(*) FROM dbo.Users WHERE MustChangePassword = 1),
        (SELECT COUNT(*) FROM dbo.Users WHERE Password IS NOT NULL AND LEN(LTRIM(RTRIM(Password))) > 0);",
    connection))
await using (var reader = await verify.ExecuteReaderAsync())
{
    await reader.ReadAsync();
    Console.WriteLine();
    Console.WriteLine($"Accounts with a hash          : {reader.GetInt32(0)}");
    Console.WriteLine($"Accounts owing a reset        : {reader.GetInt32(1)}");
    Console.WriteLine($"Plain-text passwords remaining: {reader.GetInt32(2)}");
}

return failed == 0 ? 0 : 1;
