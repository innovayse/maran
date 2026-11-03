using System.Security.Claims;
using Maran.Modules.Identity.Authorization;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Maran.Modules.Identity.Tests.Authorization;

/// <summary>Behavioural contract of the view-as allow-list: a view-as token reaches only what an endpoint declares (issues #317, #334).</summary>
public sealed class ViewAsBoundaryHandlerTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static DefaultHttpContext Request(ViewAsAccess? access, string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        var metadata = access is { } declared
            ? new EndpointMetadataCollection(new ViewAsAccessAttribute(declared))
            : new EndpointMetadataCollection();
        context.SetEndpoint(new Endpoint(null, metadata, "test"));
        return context;
    }

    private static ClaimsPrincipal ViewAsToken()
    {
        return Principal(new Claim(PanelClaimTypes.Actor, "{}"), new Claim(PanelClaimTypes.SessionKind, "ViewAs"));
    }

    private static async Task<bool> EvaluateAsync(ClaimsPrincipal user, HttpContext request)
    {
        var requirement = new ViewAsBoundaryRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, request);
        await new ViewAsBoundaryHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    /// <summary>A view-as token is refused on an endpoint that declares nothing: the allow-list's default.</summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task A_view_as_token_is_refused_on_an_undeclared_endpoint(string method)
    {
        Assert.False(await EvaluateAsync(ViewAsToken(), Request(access: null, method)));
    }

    /// <summary>A view-as token is refused on an endpoint declared Refused.</summary>
    [Fact]
    public async Task A_view_as_token_is_refused_on_a_refused_endpoint()
    {
        Assert.False(await EvaluateAsync(ViewAsToken(), Request(ViewAsAccess.Refused, "POST")));
    }

    /// <summary>A view-as token reaches a declared read on a safe method.</summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task A_view_as_token_reaches_a_declared_read_on_a_safe_method(string method)
    {
        Assert.True(await EvaluateAsync(ViewAsToken(), Request(ViewAsAccess.Read, method)));
    }

    /// <summary>A read declaration does not carry a mutation through, whatever the method.</summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task A_read_declaration_does_not_let_a_mutating_method_through(string method)
    {
        Assert.False(await EvaluateAsync(ViewAsToken(), Request(ViewAsAccess.Read, method)));
    }

    /// <summary>A view-as token reaches a declared safe mutation.</summary>
    [Fact]
    public async Task A_view_as_token_reaches_a_declared_safe_mutation()
    {
        Assert.True(await EvaluateAsync(ViewAsToken(), Request(ViewAsAccess.SafeMutation, "POST")));
    }

    /// <summary>Either marker alone is enough to restrict, so a token missing one is still on the allow-list.</summary>
    [Fact]
    public async Task Either_marker_alone_is_enough_to_restrict()
    {
        Assert.False(await EvaluateAsync(Principal(new Claim(PanelClaimTypes.Actor, "{}")), Request(null, "POST")));
        Assert.False(await EvaluateAsync(Principal(new Claim(PanelClaimTypes.SessionKind, "ViewAs")), Request(null, "POST")));
    }

    /// <summary>Outside the request pipeline a view-as token is left unmet: there is no metadata to allow it.</summary>
    [Fact]
    public async Task A_view_as_token_is_left_unmet_when_the_resource_is_not_a_request()
    {
        var requirement = new ViewAsBoundaryRequirement();
        var context = new AuthorizationHandlerContext([requirement], ViewAsToken(), resource: null);

        await new ViewAsBoundaryHandler().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    /// <summary>An own token passes every endpoint: the allow-list restricts viewing only.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(ViewAsAccess.Refused)]
    public async Task An_own_token_passes_every_endpoint(ViewAsAccess? access)
    {
        Assert.True(await EvaluateAsync(Principal(new Claim(PanelClaimTypes.UserId, "u")), Request(access, "POST")));
    }
}
