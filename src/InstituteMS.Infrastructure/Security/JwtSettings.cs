namespace InstituteMS.Infrastructure.Security;

public class JwtSettings
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "InstituteMS";
    public string Audience { get; set; } = "InstituteMS.Client";

    /// <summary>
    /// Signing key. Development uses the value in appsettings.Development.json;
    /// production must supply it via environment variable or Key Vault (D12).
    /// </summary>
    public string Key { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>Failed attempts before the account is locked.</summary>
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
}
