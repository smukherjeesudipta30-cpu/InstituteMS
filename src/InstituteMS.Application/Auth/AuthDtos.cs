namespace InstituteMS.Application.Auth;

/// <param name="School">
/// Which school's sign-in page was used: "computer" or "preschool". Each
/// turns away the other school's accounts (SuperAdmin passes both). Left out
/// by the Android app, which signs in to either.
/// </param>
public record LoginRequest(string Email, string Password, string? School = null);

public record RefreshRequest(string RefreshToken);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Step one of account recovery: which account has lost its password.</summary>
public record ForgotPasswordRequest(string Email);

/// <summary>
/// Step two: the code just emailed, and the password to replace the forgotten one.
/// </summary>
public record ResetPasswordRequest(string Email, string Code, string NewPassword);

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
    string LandingRoute,
    /// <summary>
    /// True while the account still holds a password carried over from the legacy
    /// system. The client must send the user straight to the change-password screen;
    /// the server refuses everything else until it is cleared, so this is a hint for
    /// the UI rather than the enforcement itself.
    /// </summary>
    bool MustChangePassword);

public record AuthResultDto(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    AuthUserDto User);
