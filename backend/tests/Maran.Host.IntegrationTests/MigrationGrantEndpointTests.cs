using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The classifier the credential walks use to leave out the export-grant endpoints is itself shown to
/// refuse what is not one of them, and to accept what is: a classifier that matched everything would
/// silently empty every walk that relies on it.
/// </summary>
public sealed class MigrationGrantEndpointTests
{
    /// <summary>An endpoint naming the grant scheme and its pull policy is recognised.</summary>
    [Fact]
    public void An_endpoint_naming_the_grant_scheme_and_pull_policy_is_grant_only()
    {
        var endpoint = EndpointOf(
            new AuthorizeAttribute(MigrationGrantPolicyNames.Pull) { AuthenticationSchemes = MigrationGrantDefaults.Scheme });

        Assert.True(MigrationGrantEndpoint.IsGrantOnly(endpoint));
    }

    /// <summary>An endpoint a login reaches, and one with no declaration at all, are not.</summary>
    [Fact]
    public void An_endpoint_a_login_reaches_is_not_grant_only()
    {
        var withPolicy = EndpointOf(new AuthorizeAttribute(AuthorizationPolicies.AdminOnly));
        var withoutDeclaration = EndpointOf();
        var grantSchemeOtherPolicy = EndpointOf(
            new AuthorizeAttribute(AuthorizationPolicies.AdminOnly) { AuthenticationSchemes = MigrationGrantDefaults.Scheme });

        Assert.False(MigrationGrantEndpoint.IsGrantOnly(withPolicy));
        Assert.False(MigrationGrantEndpoint.IsGrantOnly(withoutDeclaration));
        Assert.False(MigrationGrantEndpoint.IsGrantOnly(grantSchemeOtherPolicy));
    }

    /// <summary>Builds a route endpoint carrying the given authorize declarations.</summary>
    /// <param name="declarations">The authorize attributes to attach as metadata.</param>
    /// <returns>The endpoint.</returns>
    private static RouteEndpoint EndpointOf(params IAuthorizeData[] declarations)
    {
        return new RouteEndpoint(
            _ => { return Task.CompletedTask; },
            RoutePatternFactory.Parse("/probe"),
            0,
            new EndpointMetadataCollection(declarations),
            "probe");
    }
}
