using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Decides <see cref="ViewAsBoundaryRequirement"/>: a token carrying a view-as marker may not reach an
/// endpoint marked <see cref="RefusedWhileViewingAsAttribute"/>; every other caller and endpoint passes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Either marker counts.</b> A token carrying <see cref="PanelClaimTypes.Actor"/> or
/// <see cref="PanelClaimTypes.SessionKind"/> is a view-as token here, so a token missing one of the
/// two is still restricted rather than mistaken for an own sign-in. Whether the session behind it is
/// still live is not decided here; the bearer handler already refused it if not.
/// </para>
/// <para>
/// <b>Outside the request pipeline the requirement is left unmet</b> for a view-as token, as the
/// two-factor handler does: without the endpoint's metadata there is no way to know it is not a
/// refused one, and this control's job is to refuse.
/// </para>
/// </remarks>
public sealed class ViewAsBoundaryHandler : AuthorizationHandler<ViewAsBoundaryRequirement>
{
    /// <summary>Succeeds unless a view-as token is calling a refused endpoint.</summary>
    /// <param name="context">The authorization context; its resource is the current request.</param>
    /// <param name="requirement">The requirement being evaluated.</param>
    /// <returns>Resolves once the requirement has been marked succeeded or left unmet.</returns>
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ViewAsBoundaryRequirement requirement)
    {
        var viewingAs = context.User.HasClaim(claim =>
        {
            return claim.Type == PanelClaimTypes.Actor || claim.Type == PanelClaimTypes.SessionKind;
        });

        if (!viewingAs)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.Resource is HttpContext httpContext
            && httpContext.GetEndpoint() is { } endpoint
            && endpoint.Metadata.GetMetadata<RefusedWhileViewingAsAttribute>() is null)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
