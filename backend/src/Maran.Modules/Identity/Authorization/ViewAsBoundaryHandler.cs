using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Modules.Identity.Authorization;

/// <summary>
/// Decides <see cref="ViewAsBoundaryRequirement"/>: a token carrying a view-as marker may reach an
/// endpoint only when it declares, with <see cref="ViewAsAccessAttribute"/>, a read on a safe method or
/// a safe mutation; every other caller passes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Either marker counts.</b> A token carrying <see cref="PanelClaimTypes.Actor"/> or
/// <see cref="PanelClaimTypes.SessionKind"/> is a view-as token here, so a token missing one of the
/// two is still restricted rather than mistaken for an own sign-in. Whether the session behind it is
/// still live is not decided here; the bearer handler already refused it if not.
/// </para>
/// <para>
/// <b>An allow-list.</b> An endpoint with no declaration is refused for a view-as token (issue #334).
/// The deny-list this replaced let a view-as session create SFTP logins (and read back the password),
/// databases and cron entries, because nothing had marked them. The action's declaration wins over its
/// controller's: ASP.NET Core orders endpoint metadata controller first, and
/// <c>GetMetadata</c> returns the last match.
/// </para>
/// <para>
/// <b>A read is honoured for GET and HEAD only.</b> <see cref="ViewAsAccess.Read"/> on any other method
/// is refused, so a declaration cannot make a mutation pass by naming it a read.
/// </para>
/// <para>
/// <b>Outside the request pipeline the requirement is left unmet</b> for a view-as token, as the
/// two-factor handler does: without the endpoint's metadata there is nothing to allow, and this
/// control's job is to refuse.
/// </para>
/// </remarks>
public sealed class ViewAsBoundaryHandler : AuthorizationHandler<ViewAsBoundaryRequirement>
{
    /// <summary>Succeeds for an own token, and for a view-as token only on an endpoint that allows it.</summary>
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
            && IsAllowed(endpoint.Metadata.GetMetadata<ViewAsAccessAttribute>()?.Access, httpContext.Request.Method))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    /// <summary>Whether a declared access lets a view-as token through for a request method.</summary>
    /// <param name="access">The endpoint's declaration, or <c>null</c> when it carries none.</param>
    /// <param name="method">The HTTP method of the request.</param>
    /// <returns><c>true</c> for a read on a safe method or a safe mutation; <c>false</c> otherwise, undeclared included.</returns>
    private static bool IsAllowed(ViewAsAccess? access, string method)
    {
        return access switch
        {
            ViewAsAccess.Read => HttpMethods.IsGet(method) || HttpMethods.IsHead(method),
            ViewAsAccess.SafeMutation => true,
            _ => false,
        };
    }
}
