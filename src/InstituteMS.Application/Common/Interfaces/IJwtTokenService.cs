using System.Security.Claims;
using InstituteMS.Domain.Entities;

namespace InstituteMS.Application.Common.Interfaces;

public interface IJwtTokenService
{
    /// <summary>Issues a signed access token and reports its lifetime in seconds.</summary>
    (string Token, int ExpiresInSeconds) CreateAccessToken(User user);

    /// <summary>Cryptographically random opaque refresh token.</summary>
    string CreateRefreshToken();

    /// <summary>
    /// Validates an access token ignoring its expiry — used by the refresh flow to
    /// tie a refresh token back to the user it was issued for.
    /// </summary>
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
