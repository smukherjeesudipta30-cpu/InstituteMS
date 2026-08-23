using InstituteMS.Application.Common.Interfaces;
using InstituteMS.Domain.Common;
using InstituteMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Infrastructure.Persistence;

/// <summary>
/// Creates the first Admin account if <c>dbo.Users</c> is empty. The password is
/// hashed here rather than pinned as a literal hash in a .sql file so nothing
/// reusable ends up in source control.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbSeeder));

        if (!await db.Database.CanConnectAsync(ct))
        {
            logger.LogError("Cannot reach the database — skipping seed.");
            return;
        }

        var email = config["Seed:AdminEmail"] ?? "admin@avijan.local";
        var password = config["Seed:AdminPassword"] ?? "Admin@12345";

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (existing is not null)
        {
            if (string.IsNullOrWhiteSpace(existing.PasswordHash))
            {
                existing.PasswordHash = hasher.Hash(password);
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Admin {Email} had no password hash — one was generated.", email);
            }
            return;
        }

        if (await db.Users.AnyAsync(ct))
        {
            logger.LogInformation("Users table is already populated — not seeding the default admin.");
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        db.Users.Add(new User
        {
            Name = "System Administrator",
            Email = email,
            Roles = UserRoles.Admin,
            LoginId = "admin",
            IsActive = true,
            RegisterDate = today,
            PasswordHash = hasher.Hash(password),
            CreatedBy = "seed",
            CreatedOn = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Seeded default admin {Email}. Change this password immediately via POST /api/v1/auth/change-password.",
            email);
    }
}
