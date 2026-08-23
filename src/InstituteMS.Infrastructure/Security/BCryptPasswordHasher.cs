using InstituteMS.Application.Common.Interfaces;

namespace InstituteMS.Infrastructure.Security;

/// <summary>
/// Replaces the legacy plain-text password comparison in
/// <c>Models/Repository.cs:17</c> (defect D3).
/// </summary>
public class BCryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
        {
            // Still run a hash so a missing-user path costs the same as a wrong-password
            // path — otherwise response timing leaks which emails exist.
            BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            // A legacy row whose Password column was copied across without hashing.
            return false;
        }
    }
}
