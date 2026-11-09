using System.Security.Claims;
using Maran.Modules.Identity.Authorization;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Maran.Modules.Identity.Tests.Authorization;

/// <summary>
/// Behavioural contract of the first-password gate: a token carrying the claim reaches only the endpoints
/// marked <see cref="AllowDuringPasswordChangeAttribute"/>, and every other token is left alone.
/// </summary>
public sealed class PasswordChangeCompleteHandlerTests
{
    /// <summary>Builds an authenticated principal holding the given claims.</summary>
    /// <param name="claims">The claims on the token.</param>
    /// <returns>The principal.</returns>
    private static ClaimsPrincipal Authenticated(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    /// <summary>A principal whose token carries the password-change claim.</summary>
    /// <returns>The restricted principal.</returns>
    private static ClaimsPrincipal Restricted()
    {
        return Authenticated(new Claim(PanelClaimTypes.PasswordChangeRequired, "true"));
    }

    /// <summary>Builds a request whose endpoint carries (or lacks) the exemption marker.</summary>
    /// <param name="exempt">Whether the endpoint is marked <see cref="AllowDuringPasswordChangeAttribute"/>.</param>
    /// <returns>The request context.</returns>
    private static DefaultHttpContext Request(bool exempt)
    {
        var context = new DefaultHttpContext();
        var metadata = exempt
            ? new EndpointMetadataCollection(new AllowDuringPasswordChangeAttribute())
            : new EndpointMetadataCollection();
        context.SetEndpoint(new Endpoint(null, metadata, "test"));
        return context;
    }

    /// <summary>Runs the handler and reports whether the requirement succeeded.</summary>
    /// <param name="user">The caller.</param>
    /// <param name="resource">The authorization resource.</param>
    /// <returns>True when the requirement was met.</returns>
    private static async Task<bool> EvaluateAsync(ClaimsPrincipal user, object? resource)
    {
        var requirement = new PasswordChangeCompleteRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, resource);
        await new PasswordChangeCompleteHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    /// <summary>A restricted token is refused on an endpoint that is not the password change.</summary>
    [Fact]
    public async Task A_restricted_token_is_refused_on_an_ordinary_endpoint()
    {
        Assert.False(await EvaluateAsync(Restricted(), Request(exempt: false)));
    }

    /// <summary>A restricted token reaches an endpoint marked as part of the password change.</summary>
    /// <remarks>The inverse control: a handler mutated to refuse everything passes the test above.</remarks>
    [Fact]
    public async Task A_restricted_token_reaches_an_endpoint_marked_as_the_password_change()
    {
        Assert.True(await EvaluateAsync(Restricted(), Request(exempt: true)));
    }

    /// <summary>A token without the claim reaches every endpoint, marked or not.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_token_without_the_claim_is_not_restricted(bool exempt)
    {
        Assert.True(await EvaluateAsync(Authenticated(new Claim(PanelClaimTypes.Role, "Customer")), Request(exempt)));
    }

    /// <summary>An unauthenticated caller is not this requirement's business: the policy answers 401.</summary>
    [Fact]
    public async Task An_unauthenticated_caller_is_left_to_the_policy()
    {
        Assert.True(await EvaluateAsync(new ClaimsPrincipal(new ClaimsIdentity()), Request(exempt: false)));
    }

    /// <summary>Outside the request pipeline a restricted token is left unmet: there is no metadata to allow it.</summary>
    [Fact]
    public async Task A_restricted_token_is_left_unmet_when_the_resource_is_not_a_request()
    {
        Assert.False(await EvaluateAsync(Restricted(), resource: null));
    }

    /// <summary>The claim's value is not parsed: its presence alone is the restriction.</summary>
    [Fact]
    public async Task The_claims_presence_alone_is_the_restriction()
    {
        var odd = Authenticated(new Claim(PanelClaimTypes.PasswordChangeRequired, "false"));

        Assert.False(await EvaluateAsync(odd, Request(exempt: false)));
    }
}
