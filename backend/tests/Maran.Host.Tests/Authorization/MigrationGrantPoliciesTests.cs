using System.Security.Claims;
using Maran.Host.Authorization;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Authorization;

/// <summary>
/// The export-grant policy admits a principal the grant handler made and nothing else: no login, no API key, and a
/// claim of the grant's name on a principal of another scheme is not enough (issue #127).
/// </summary>
public sealed class MigrationGrantPoliciesTests
{
    /// <summary>The service that evaluates a policy over a principal, configured with the grant policy only.</summary>
    private readonly IAuthorizationService _authorization;

    /// <summary>Builds the authorization service over the grant policy.</summary>
    public MigrationGrantPoliciesTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(MigrationGrantPolicies.Configure);
        _authorization = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    /// <summary>The policy as registered, to read the schemes it names.</summary>
    private static AuthorizationPolicy RegisteredPolicy()
    {
        var options = new AuthorizationOptions();
        MigrationGrantPolicies.Configure(options);

        return options.GetPolicy(MigrationGrantPolicyNames.Pull)!;
    }

    /// <summary>Whether the principal passes the pull policy.</summary>
    private async Task<bool> PassesAsync(ClaimsPrincipal principal)
    {
        return (await _authorization.AuthorizeAsync(principal, null, MigrationGrantPolicyNames.Pull)).Succeeded;
    }

    /// <summary>The accepting control: a principal of the grant scheme holding both claims passes.</summary>
    [Fact]
    public async Task A_grant_principal_with_both_claims_passes()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PanelClaimTypes.MigrationGrantId, Guid.NewGuid().ToString()), new Claim(PanelClaimTypes.MigrationExportId, Guid.NewGuid().ToString())],
            MigrationGrantDefaults.Scheme));

        Assert.True(await PassesAsync(principal));
    }

    /// <summary>The policy names the grant scheme and no other, so a login's token is never evaluated against it.</summary>
    [Fact]
    public void The_policy_names_the_grant_scheme_only()
    {
        Assert.Equal([MigrationGrantDefaults.Scheme], RegisteredPolicy().AuthenticationSchemes);
    }

    /// <summary>A principal missing either claim, and an anonymous one, are refused.</summary>
    [Fact]
    public async Task A_principal_missing_a_claim_or_anonymous_is_refused()
    {
        var onlyGrant = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PanelClaimTypes.MigrationGrantId, Guid.NewGuid().ToString())], MigrationGrantDefaults.Scheme));
        var onlyExport = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PanelClaimTypes.MigrationExportId, Guid.NewGuid().ToString())], MigrationGrantDefaults.Scheme));

        Assert.False(await PassesAsync(onlyGrant));
        Assert.False(await PassesAsync(onlyExport));
        Assert.False(await PassesAsync(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    /// <summary>A login's principal, and an API key's, do not pass: they hold neither claim.</summary>
    [Fact]
    public async Task A_login_and_an_api_key_are_refused()
    {
        var login = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PanelClaimTypes.UserId, Guid.NewGuid().ToString()), new Claim(PanelClaimTypes.Role, "Admin")], "Bearer"));
        var key = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PanelClaimTypes.ApiKeyId, Guid.NewGuid().ToString())], ApiKeyDefaults.Scheme));

        Assert.False(await PassesAsync(login));
        Assert.False(await PassesAsync(key));
    }
}
