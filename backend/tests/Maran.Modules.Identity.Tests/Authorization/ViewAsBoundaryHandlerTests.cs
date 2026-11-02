using System.Security.Claims;
using Maran.Modules.Identity.Authorization;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Maran.Modules.Identity.Tests.Authorization;

/// <summary>Behavioural contract of the endpoints a view-as token may not reach (issue #317).</summary>
public sealed class ViewAsBoundaryHandlerTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static DefaultHttpContext Request(bool refused)
    {
        var context = new DefaultHttpContext();
        var metadata = refused
            ? new EndpointMetadataCollection(new RefusedWhileViewingAsAttribute())
            : new EndpointMetadataCollection();
        context.SetEndpoint(new Endpoint(null, metadata, "test"));
        return context;
    }

    private static async Task<bool> EvaluateAsync(ClaimsPrincipal user, HttpContext request)
    {
        var requirement = new ViewAsBoundaryRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, request);
        await new ViewAsBoundaryHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    /// <summary>A view-as token is refused on a marked endpoint.</summary>
    [Fact]
    public async Task A_view_as_token_is_refused_on_a_marked_endpoint()
    {
        var user = Principal(new Claim(PanelClaimTypes.Actor, "{}"), new Claim(PanelClaimTypes.SessionKind, "ViewAs"));

        Assert.False(await EvaluateAsync(user, Request(refused: true)));
    }

    /// <summary>Either marker alone is enough to refuse, so a token missing one is still restricted.</summary>
    [Fact]
    public async Task Either_marker_alone_is_enough_to_refuse()
    {
        Assert.False(await EvaluateAsync(Principal(new Claim(PanelClaimTypes.Actor, "{}")), Request(refused: true)));
        Assert.False(await EvaluateAsync(Principal(new Claim(PanelClaimTypes.SessionKind, "ViewAs")), Request(refused: true)));
    }

    /// <summary>A view-as token passes an unmarked endpoint: viewing lets the operator use the panel.</summary>
    [Fact]
    public async Task A_view_as_token_passes_an_unmarked_endpoint()
    {
        var user = Principal(new Claim(PanelClaimTypes.Actor, "{}"), new Claim(PanelClaimTypes.SessionKind, "ViewAs"));

        Assert.True(await EvaluateAsync(user, Request(refused: false)));
    }

    /// <summary>An own token passes a marked endpoint: the mark restricts viewing only.</summary>
    [Fact]
    public async Task An_own_token_passes_a_marked_endpoint()
    {
        Assert.True(await EvaluateAsync(Principal(new Claim(PanelClaimTypes.UserId, "u")), Request(refused: true)));
    }
}
