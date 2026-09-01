using InstituteMS.Api.Filters;
using InstituteMS.Application.Auth;
using InstituteMS.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Replaces the legacy <c>AccountController</c> and <c>Views/Account/Login.cshtml</c>.
/// Token-based so the same endpoints serve the Angular app and the future APK.
/// </summary>
[Route("api/v1/auth")]
[Tags("Auth")]
public class AuthController : ApiControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>Signs a user in and issues an access + refresh token pair.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<AuthResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResultDto>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
        => Envelope(await _auth.LoginAsync(request, ct), "Signed in.");

    /// <summary>Exchanges a valid refresh token for a new token pair (the old one is revoked).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(ApiResponse<AuthResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken ct)
        => Envelope(await _auth.RefreshAsync(request, ct));

    /// <summary>Revokes the caller's refresh token.</summary>
    [HttpPost("logout")]
    [AllowWhilePasswordResetPending]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (CurrentUserId is { } userId)
            await _auth.LogoutAsync(userId, ct);

        return Envelope<object?>(null, "Signed out.");
    }

    /// <summary>The signed-in user — replaces <c>Session["UserID"]</c> (defect D8).</summary>
    [HttpGet("me")]
    [AllowWhilePasswordResetPending]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<AuthUserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
            return Envelope(Result<AuthUserDto>.Failure("Not authenticated.", ResultStatus.Unauthorized));

        return Envelope(await _auth.GetCurrentUserAsync(userId, ct));
    }

    /// <summary>Changes the caller's password and revokes their other sessions.</summary>
    [HttpPost("change-password")]
    [AllowWhilePasswordResetPending]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        if (CurrentUserId is not { } userId)
            return Envelope(Result<bool>.Failure("Not authenticated.", ResultStatus.Unauthorized));

        return Envelope(await _auth.ChangePasswordAsync(userId, request, ct), "Password changed.");
    }
}
