using System.Security.Claims;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace InstituteMS.Api.Security;

/// <summary>
/// Makes SuperAdmin a role above every screen's own <c>[Authorize(Roles=...)]</c>
/// list rather than a member of each one. ASP.NET Core runs every registered
/// handler for a requirement and succeeds if any of them does, so this sits
/// alongside the framework's own <c>RolesAuthorizationHandler</c>: that one
/// still enforces each controller's declared roles exactly as before, and this
/// one additionally succeeds whenever the caller carries the SuperAdmin role
/// claim, whatever roles the endpoint actually listed.
/// </summary>
public sealed class SuperAdminAuthorizationHandler : AuthorizationHandler<RolesAuthorizationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, RolesAuthorizationRequirement requirement)
    {
        if (context.User.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == UserRoles.SuperAdmin))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
