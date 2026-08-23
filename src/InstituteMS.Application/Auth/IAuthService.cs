using InstituteMS.Application.Common;

namespace InstituteMS.Application.Auth;

public interface IAuthService
{
    Task<Result<AuthResultDto>> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<Result<AuthResultDto>> RefreshAsync(RefreshRequest request, CancellationToken ct = default);

    Task LogoutAsync(int userId, CancellationToken ct = default);

    Task<Result<AuthUserDto>> GetCurrentUserAsync(int userId, CancellationToken ct = default);

    Task<Result<bool>> ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken ct = default);
}
