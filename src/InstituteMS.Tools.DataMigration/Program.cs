using System.Data;
using InstituteMS.Application.Common.Interfaces;
using InstituteMS.Infrastructure.Security;
using Microsoft.Data.SqlClient;

/*
 * InstituteMS.Tools.DataMigration
 * ------------------------------------------------------------------------
 * Migrates dbo.Users from the legacy database, replacing each plain-text
 * password with a BCrypt hash. This is MIGRATION_PLAN §2.3 script 004
 * ("run by a small console tool, not T-SQL") -- hashing cannot be done in
 * SQL Server, which is why this is not a .sql file like the others.
 *
 * Existing logins keep working: the legacy password is hashed, not reset,
 * so every user signs in with the credentials they already have. The
 * plain-text column is never written to the target database.
 *
 * Idempotent: clears dbo.Users and reloads it, then re-creates the local
 * admin account.
 *
 * Usage (from the repository root):
 *   dotnet run --project Avijan_Migration_API/src/InstituteMS.Tools.DataMigration
 *   dotnet run --project ... -- --dry-run
 */

var arguments = args.ToList();
var dryRun = arguments.Remove("--dry-run");

var sourceDb = ValueFor("--source") ?? "ph19987796653_";
var targetDb = ValueFor("--target") ?? "Avijan_Angular_Migration";
var server = ValueFor("--server") ?? ".";
var adminEmail = ValueFor("--admin-email") ?? "admin@avijan.local";
var adminPassword = ValueFor("--admin-password") ?? "Admin@12345";

var sourceConnectionString = Connection(server, sourceDb);
var targetConnectionString = Connection(server, targetDb);

IPasswordHasher hasher = new BCryptPasswordHasher();

Console.WriteLine($"Source : {sourceDb}");
Console.WriteLine($"Target : {targetDb}");
if (dryRun) Console.WriteLine("Mode   : DRY RUN (nothing will be written)");
Console.WriteLine();

var users = await ReadLegacyUsersAsync(sourceConnectionString);
Console.WriteLine($"Legacy users read : {users.Count}");

var missingPassword = users.Count(u => string.IsNullOrWhiteSpace(u.Password));
var blankEmail = users.Count(u => string.IsNullOrWhiteSpace(u.Email));

var duplicateEmails = users
    .Where(u => !string.IsNullOrWhiteSpace(u.Email))
    .GroupBy(u => u.Email!.Trim(), StringComparer.OrdinalIgnoreCase)
    .Where(g => g.Count() > 1)
    .ToList();

var roleFixes = users.Count(u => NormaliseRole(u.Roles) != (u.Roles ?? string.Empty).Trim());

Console.WriteLine($"  without a password : {missingPassword}");
Console.WriteLine($"  without an email   : {blankEmail}");
Console.WriteLine($"  duplicate emails   : {duplicateEmails.Count} address(es)");
Console.WriteLine($"  role values fixed  : {roleFixes}");
Console.WriteLine();

foreach (var group in duplicateEmails)
{
    Console.WriteLine($"  ! '{group.Key}' is used by ids {string.Join(", ", group.Select(u => u.Id))}");
}

if (duplicateEmails.Count > 0)
{
    Console.WriteLine("    All are migrated. Login resolves to the most recently registered active account.");
    Console.WriteLine();
}

Console.Write($"Hashing {users.Count} passwords with BCrypt (work factor 12)");

var hashed = new List<(LegacyUser User, string? Hash)>(users.Count);
var progress = 0;

foreach (var user in users)
{
    // Users with no password get no hash: AuthService rejects a null hash, so the
    // account exists but cannot sign in until an administrator sets a password.
    var hash = string.IsNullOrWhiteSpace(user.Password) ? null : hasher.Hash(user.Password!);
    hashed.Add((user, hash));

    if (++progress % 25 == 0) Console.Write(".");
}

Console.WriteLine(" done.");
Console.WriteLine();

if (dryRun)
{
    Console.WriteLine("Dry run complete - no changes written.");
    return 0;
}

var inserted = await WriteUsersAsync(targetConnectionString, hashed, adminEmail, adminPassword, hasher);

Console.WriteLine($"Users written  : {inserted}");
Console.WriteLine($"Admin ensured  : {adminEmail}");
Console.WriteLine();
Console.WriteLine("Every migrated user signs in with their existing password.");
Console.WriteLine("No plain-text password was copied into the target database.");

return 0;

string? ValueFor(string name)
{
    var index = arguments.IndexOf(name);
    return index >= 0 && index + 1 < arguments.Count ? arguments[index + 1] : null;
}

static string Connection(string server, string database) =>
    $"Server={server};Database={database};Integrated Security=SSPI;TrustServerCertificate=True;Encrypt=False";

/// <summary>
/// Legacy role values are messier than the four in the plan: a 'sudent' typo, and
/// 'Receptionist' / 'Receptionist &amp; Teacher' which the plan never listed.
/// Only the typo is corrected; the real extra roles are preserved verbatim and
/// handled by UserRoles on the API side.
/// </summary>
static string NormaliseRole(string? role)
{
    var value = (role ?? string.Empty).Trim();

    return value.ToLowerInvariant() switch
    {
        "sudent" => "Student",
        "student" => "Student",
        "admin" => "Admin",
        "editor" => "Editor",
        "teacher" => "Teacher",
        "receptionist" => "Receptionist",
        _ => value
    };
}

static async Task<List<LegacyUser>> ReadLegacyUsersAsync(string connectionString)
{
    const string sql = """
        SELECT id, Email, Roles, Password, IsActive, Loginid,
               Registerdate, End_date, Name, Phn_no, User_date_of_birth
        FROM   dbo.Users
        ORDER BY id
        """;

    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var command = new SqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();

    var users = new List<LegacyUser>();

    while (await reader.ReadAsync())
    {
        users.Add(new LegacyUser
        {
            Id = reader.GetInt32(0),
            Email = reader.IsDBNull(1) ? null : reader.GetString(1).Trim(),
            Roles = reader.IsDBNull(2) ? null : reader.GetString(2),
            Password = reader.IsDBNull(3) ? null : reader.GetString(3),
            IsActive = !reader.IsDBNull(4) && reader.GetBoolean(4),
            LoginId = reader.IsDBNull(5) ? null : reader.GetString(5).Trim(),
            RegisterDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            EndDate = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            Name = reader.IsDBNull(8) ? null : reader.GetString(8).Trim(),
            Phone = reader.IsDBNull(9) ? null : reader.GetString(9).Trim(),
            DateOfBirth = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
        });
    }

    return users;
}

static async Task<int> WriteUsersAsync(
    string connectionString,
    List<(LegacyUser User, string? Hash)> users,
    string adminEmail,
    string adminPassword,
    IPasswordHasher hasher)
{
    await using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    await using var transaction = await connection.BeginTransactionAsync();

    try
    {
        await Execute(connection, transaction, "DELETE FROM dbo.Users;");
        await Execute(connection, transaction, "SET IDENTITY_INSERT dbo.Users ON;");

        const string insert = """
            INSERT INTO dbo.Users
                (id, Name, Email, Roles, Loginid, Phn_no, User_date_of_birth,
                 Registerdate, End_date, IsActive, PasswordHash, FailedLoginCount,
                 CreatedBy, CreatedOn)
            VALUES
                (@id, @Name, @Email, @Roles, @LoginId, @Phone, @DateOfBirth,
                 @RegisterDate, @EndDate, @IsActive, @PasswordHash, 0,
                 'migration-users', SYSDATETIME());
            """;

        var count = 0;

        foreach (var (user, hash) in users)
        {
            await using var command = new SqlCommand(insert, connection, (SqlTransaction)transaction);

            command.Parameters.Add("@id", SqlDbType.Int).Value = user.Id;
            AddNullable(command, "@Name", SqlDbType.VarChar, 250, user.Name);
            AddNullable(command, "@Email", SqlDbType.VarChar, 50, string.IsNullOrWhiteSpace(user.Email) ? null : user.Email);
            AddNullable(command, "@Roles", SqlDbType.VarChar, 150, NormaliseRole(user.Roles));
            AddNullable(command, "@LoginId", SqlDbType.VarChar, 20, user.LoginId);
            AddNullable(command, "@Phone", SqlDbType.VarChar, 50, user.Phone);
            AddNullable(command, "@DateOfBirth", SqlDbType.Date, 0, user.DateOfBirth);
            AddNullable(command, "@RegisterDate", SqlDbType.Date, 0, user.RegisterDate);
            AddNullable(command, "@EndDate", SqlDbType.Date, 0, user.EndDate);
            command.Parameters.Add("@IsActive", SqlDbType.Bit).Value = user.IsActive;
            AddNullable(command, "@PasswordHash", SqlDbType.NVarChar, 255, hash);

            await command.ExecuteNonQueryAsync();
            count++;
        }

        await Execute(connection, transaction, "SET IDENTITY_INSERT dbo.Users OFF;");

        // Re-create the local administrator so the new stack always has a way in,
        // even if none of the legacy admin passwords are known.
        var ensureAdmin = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Email = @Email)
            INSERT INTO dbo.Users
                (Name, Email, Roles, Loginid, IsActive, Registerdate, PasswordHash, FailedLoginCount, CreatedBy, CreatedOn)
            VALUES
                ('System Administrator', @Email, 'Admin', 'admin', 1, CAST(GETDATE() AS date), @Hash, 0, 'migration-users', SYSDATETIME());
            """, connection, (SqlTransaction)transaction);

        ensureAdmin.Parameters.Add("@Email", SqlDbType.VarChar, 50).Value = adminEmail;
        ensureAdmin.Parameters.Add("@Hash", SqlDbType.NVarChar, 255).Value = hasher.Hash(adminPassword);
        await ensureAdmin.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
        return count;
    }
    catch
    {
        await transaction.RollbackAsync();
        throw;
    }

    static async Task Execute(SqlConnection connection, System.Data.Common.DbTransaction transaction, string sql)
    {
        await using var command = new SqlCommand(sql, connection, (SqlTransaction)transaction);
        await command.ExecuteNonQueryAsync();
    }

    static void AddNullable(SqlCommand command, string name, SqlDbType type, int size, object? value)
    {
        var parameter = size > 0
            ? command.Parameters.Add(name, type, size)
            : command.Parameters.Add(name, type);

        parameter.Value = value is null || (value is string s && string.IsNullOrWhiteSpace(s))
            ? DBNull.Value
            : value;
    }
}

internal sealed class LegacyUser
{
    public int Id { get; init; }
    public string? Email { get; init; }
    public string? Roles { get; init; }
    public string? Password { get; init; }
    public bool IsActive { get; init; }
    public string? LoginId { get; init; }
    public DateTime? RegisterDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string? Name { get; init; }
    public string? Phone { get; init; }
    public DateTime? DateOfBirth { get; init; }
}
