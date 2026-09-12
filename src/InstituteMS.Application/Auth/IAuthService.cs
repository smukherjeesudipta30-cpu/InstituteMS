using InstituteMS.Application.Common;

namespace InstituteMS.Application.Auth;

public interface IAuthService
{
    Task<Result<AuthResultDto>> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<Result<AuthResultDto>> RefreshAsync(RefreshRequest request, CancellationToken ct = default);

    Task LogoutAsync(int userId, CancellationToken ct = default);

    Task<Result<AuthUserDto>> GetCurrentUserAsync(int userId, CancellationToken ct = default);

    Task<Result<bool>> ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default);

    /// <summary>
    /// Emails a reset code to the account's personal address, if the account exists.
    /// Always succeeds from the caller's point of view — whether the address matched
    /// anything is not something the response may reveal.
    /// </summary>
    Task<Result<bool>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);

    /// <summary>Spends the emailed code to set a new password without the old one.</summary>
    Task<Result<bool>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
}
