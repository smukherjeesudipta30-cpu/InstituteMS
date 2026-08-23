using System.Diagnostics;
using System.Security.Claims;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    protected string CurrentTraceId => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    /// <summary>Authenticated user id, or null on an anonymous request.</summary>
    protected int? CurrentUserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>True when the caller holds a back-office role (Admin or Editor).</summary>
    protected bool IsBackOffice =>
        User.IsInRole(UserRoles.Admin) || User.IsInRole(UserRoles.Editor);

    /// <summary>
    /// Who is performing the action, for audit columns. This is the JWT-claim
    /// replacement for the legacy <c>Session["UserID"]</c> that was passed around as
    /// <c>patCode</c> (defect D8). Truncated to the 50 chars the columns allow.
    /// </summary>
    protected string? CurrentUserName
    {
        get
        {
            var name = User.FindFirstValue(ClaimTypes.Email)
                       ?? User.FindFirstValue(ClaimTypes.Name);

            return string.IsNullOrWhiteSpace(name)
                ? null
                : name.Length > 50 ? name[..50] : name;
        }
    }

    protected IActionResult Envelope<T>(Result<T> result, string? successMessage = null)
    {
        if (result.Succeeded)
        {
            var ok = ApiResponse<T>.Ok(result.Value!, successMessage);
            ok.TraceId = CurrentTraceId;
            return Ok(ok);
        }

        var fail = ApiResponse<T>.Fail(result.Error ?? "Request failed.");
        fail.TraceId = CurrentTraceId;

        var status = result.Status switch
        {
            ResultStatus.Unauthorized => StatusCodes.Status401Unauthorized,
            ResultStatus.Forbidden => StatusCodes.Status403Forbidden,
            ResultStatus.NotFound => StatusCodes.Status404NotFound,
            ResultStatus.Conflict => StatusCodes.Status409Conflict,
            ResultStatus.TooManyRequests => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status400BadRequest
        };

        return StatusCode(status, fail);
    }

    protected IActionResult Envelope<T>(T data, string? message = null)
    {
        var ok = ApiResponse<T>.Ok(data, message);
        ok.TraceId = CurrentTraceId;
        return Ok(ok);
    }
}
