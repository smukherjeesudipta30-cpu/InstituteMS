using InstituteMS.Application.Common;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace InstituteMS.Api.Filters;

/// <summary>
/// Marks an endpoint as reachable while the caller still owes a password reset.
/// Only the handful needed to complete the reset itself carry this.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowWhilePasswordResetPendingAttribute : Attribute;

/// <summary>
/// Turns the forced reset into something the server enforces rather than something
/// the client is asked to respect.
/// <para>
/// A token issued to one of the migrated accounts carries
/// <c>must_change_password</c>. Until that account chooses a new password, every
/// endpoint answers 403 apart from those marked
/// <see cref="AllowWhilePasswordResetPendingAttribute"/> - so a user who skips the
/// screen, or calls the API directly with a token taken from local storage, still
/// gets nowhere. Without this the hold would be a redirect in the Angular router
/// and nothing more.
/// </para>
/// </summary>
public sealed class PasswordResetFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user?.Identity?.IsAuthenticated != true)
            return;

        if (!user.HasClaim(PasswordResetClaims.MustChangePassword, "true"))
            return;

        if (context.ActionDescriptor.EndpointMetadata
                .OfType<AllowWhilePasswordResetPendingAttribute>().Any())
            return;

        var body = ApiResponse<object?>.Fail(
            "Please choose a new password before continuing.");

        context.Result = new ObjectResult(body) { StatusCode = StatusCodes.Status403Forbidden };
    }
}
