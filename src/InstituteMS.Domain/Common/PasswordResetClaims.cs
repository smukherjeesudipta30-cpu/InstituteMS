namespace InstituteMS.Domain.Common;

/// <summary>
/// The claim that marks an access token as belonging to someone who has not yet
/// replaced a password carried over from the legacy system.
/// </summary>
public static class PasswordResetClaims
{
    /// <summary>Present, with the value "true", only while the reset is outstanding.</summary>
    public const string MustChangePassword = "must_change_password";
}
