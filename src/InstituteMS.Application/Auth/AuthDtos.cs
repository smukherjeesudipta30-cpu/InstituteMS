namespace InstituteMS.Application.Auth;

public record LoginRequest(string Email, string Password);

public record RefreshRequest(string RefreshToken);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>The signed-in user as the client sees it. Never carries a hash.</summary>
/// <param name="Role">
/// The most privileged role held, which decides the landing page. Kept singular for
/// the common case.
/// </param>
/// <param name="Roles">
/// Every role held. The legacy data stores combined values such as
/// "Receptionist &amp; Teacher", so one user can have more than one.
/// </param>
public record AuthUserDto(
    int Id,
    string Name,
    string Email,
    string Role,
    IReadOnlyList<string> Roles,
    string? LoginId,
    string LandingRoute);

public record AuthResultDto(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    AuthUserDto User);
