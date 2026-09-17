using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using InstituteMS.Application.Common.Interfaces;
using InstituteMS.Domain.Common;
using InstituteMS.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace InstituteMS.Infrastructure.Security;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;

    public JwtTokenService(IOptions<JwtSettings> settings) => _settings = settings.Value;

    public (string Token, int ExpiresInSeconds) CreateAccessToken(User user)
    {
        var expiresIn = _settings.AccessTokenMinutes * 60;
        var expiresAt = DateTime.UtcNow.AddMinutes(_settings.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name ?? user.Email ?? string.Empty)
        };

        // One claim per role: the legacy column can hold a combined value such as
        // "Receptionist & Teacher", and [Authorize(Roles = ...)] matches any of them.
        foreach (var role in UserRoles.Split(user.Roles))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        if (!string.IsNullOrWhiteSpace(user.LoginId))
            claims.Add(new Claim("loginId", user.LoginId));

        // Carried in the token so the hold survives without a database read on every
        // request. It is cleared by issuing a fresh token, which change-password does
        // by revoking the refresh token and forcing a new sign-in.
        if (user.MustChangePassword)
            claims.Add(new Claim(PasswordResetClaims.MustChangePassword, "true"));

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(SigningKey(_settings.Key), SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresIn);
    }

    public string CreateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _settings.Issuer,
            ValidateAudience = true,
            ValidAudience = _settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = SigningKey(_settings.Key),
            ValidateLifetime = false   // the refresh flow is what handles expiry
        };

        try
        {
            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(token, parameters, out var validated);

            return validated is JwtSecurityToken jwt &&
                   jwt.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase)
                ? principal
                : null;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }

    /// <summary>Shared with Program.cs so token issuing and token validation use the same key material.</summary>
    public static SymmetricSecurityKey SigningKey(string key) =>
        new(Encoding.UTF8.GetBytes(key));
}
