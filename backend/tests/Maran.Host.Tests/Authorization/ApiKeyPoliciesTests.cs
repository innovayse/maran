using System.Security.Claims;
using Maran.Host.Authorization;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Authorization;

/// <summary>
/// The API key policies decide by scope AND by owner: a key a reseller owns opens the reseller endpoints
/// and never the administrator's, and a key the administrator owns the reverse (issue #368). Every
/// refusal has its accepting control beside it.
/// </summary>
public sealed class ApiKeyPoliciesTests
{
    /// <summary>The service that evaluates a policy over a principal, configured with the key policies only.</summary>
    private readonly IAuthorizationService _authorization;

    /// <summary>Builds the authorization service over the key policies.</summary>
    public ApiKeyPoliciesTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization(ApiKeyPolicies.Configure);
        _authorization = services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    /// <summary>Builds a principal of the key scheme holding the scopes, owned by a reseller when one is given.</summary>
    private static ClaimsPrincipal Key(Guid? resellerId, params string[] scopes)
    {
        var claims = new List<Claim> { new(PanelClaimTypes.ApiKeyId, Guid.NewGuid().ToString()) };
        claims.AddRange(scopes.Select(scope =>
        {
            return new Claim(PanelClaimTypes.ApiKeyScope, scope);
        }));
        if (resellerId is { } owner)
        {
            claims.Add(new Claim(PanelClaimTypes.ApiKeyReseller, owner.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, ApiKeyDefaults.Scheme));
    }

    /// <summary>Whether the principal passes the named policy.</summary>
    private async Task<bool> PassesAsync(ClaimsPrincipal principal, string policy)
    {
        return (await _authorization.AuthorizeAsync(principal, null, policy)).Succeeded;
    }

    /// <summary>A reseller's key with the scope opens the reseller endpoints, the accepting control for every refusal below.</summary>
    [Fact]
    public async Task A_resellers_key_with_the_scope_opens_the_matching_reseller_policy()
    {
        var reseller = Guid.NewGuid();

        Assert.True(await PassesAsync(Key(reseller, ApiKeyScopes.PlansRead), ApiKeyPolicyNames.PlansRead));
        Assert.True(await PassesAsync(Key(reseller, ApiKeyScopes.AccountsRead), ApiKeyPolicyNames.AccountsRead));
        Assert.True(await PassesAsync(Key(reseller, ApiKeyScopes.SelfRead), ApiKeyPolicyNames.SelfRead));
    }

    /// <summary>A reseller's key without the scope is refused the policy of the other scope.</summary>
    [Fact]
    public async Task A_resellers_key_is_refused_the_policy_of_a_scope_it_does_not_hold()
    {
        var reseller = Guid.NewGuid();

        Assert.False(await PassesAsync(Key(reseller, ApiKeyScopes.PlansRead), ApiKeyPolicyNames.AccountsRead));
        Assert.False(await PassesAsync(Key(reseller, ApiKeyScopes.AccountsRead), ApiKeyPolicyNames.PlansRead));
    }

    /// <summary>The administrator's key holding a reseller-grade scope claim is refused the reseller policies: the second lock.</summary>
    [Fact]
    public async Task The_administrators_key_is_refused_the_reseller_policies_even_with_the_scope_claim()
    {
        var administrators = Key(null, ApiKeyScopes.PlansRead, ApiKeyScopes.AccountsRead);

        Assert.False(await PassesAsync(administrators, ApiKeyPolicyNames.PlansRead));
        Assert.False(await PassesAsync(administrators, ApiKeyPolicyNames.AccountsRead));
    }

    /// <summary>A reseller's key with a write scope opens the matching write policy: the accepting control for the refusals below.</summary>
    [Fact]
    public async Task A_resellers_key_with_a_write_scope_opens_the_matching_write_policy()
    {
        var reseller = Guid.NewGuid();

        Assert.True(await PassesAsync(Key(reseller, ApiKeyScopes.AccountsWrite), ApiKeyPolicyNames.AccountsWrite));
        Assert.True(await PassesAsync(Key(reseller, ApiKeyScopes.AccountsDelete), ApiKeyPolicyNames.AccountsDelete));
    }

    /// <summary>Write, delete and read are three separate doors: holding one never opens another.</summary>
    [Fact]
    public async Task The_write_delete_and_read_scopes_do_not_imply_one_another()
    {
        var reseller = Guid.NewGuid();
        var scopes = new[]
        {
            (ApiKeyScopes.AccountsRead, ApiKeyPolicyNames.AccountsRead),
            (ApiKeyScopes.AccountsWrite, ApiKeyPolicyNames.AccountsWrite),
            (ApiKeyScopes.AccountsDelete, ApiKeyPolicyNames.AccountsDelete),
        };

        foreach (var (held, _) in scopes)
        {
            foreach (var (_, policy) in scopes.Where(pair => { return pair.Item1 != held; }))
            {
                Assert.False(await PassesAsync(Key(reseller, held), policy));
            }
        }
    }

    /// <summary>The administrator's key holding a write scope claim is refused both write policies: the second lock.</summary>
    [Fact]
    public async Task The_administrators_key_is_refused_the_write_policies_even_with_the_scope_claim()
    {
        var administrators = Key(null, ApiKeyScopes.AccountsWrite, ApiKeyScopes.AccountsDelete);

        Assert.False(await PassesAsync(administrators, ApiKeyPolicyNames.AccountsWrite));
        Assert.False(await PassesAsync(administrators, ApiKeyPolicyNames.AccountsDelete));
    }

    /// <summary>The administrator's key with the server scope opens the server policy, the accepting control for the next test.</summary>
    [Fact]
    public async Task The_administrators_key_with_the_server_scope_opens_the_server_policy()
    {
        Assert.True(await PassesAsync(Key(null, ApiKeyScopes.ServerRead), ApiKeyPolicyNames.ServerRead));
    }

    /// <summary>A reseller's key holding the server scope claim is refused the server policy: the second lock.</summary>
    [Fact]
    public async Task A_resellers_key_is_refused_the_server_policy_even_with_the_scope_claim()
    {
        Assert.False(await PassesAsync(Key(Guid.NewGuid(), ApiKeyScopes.ServerRead), ApiKeyPolicyNames.ServerRead));
    }

    /// <summary>A principal that is not authenticated, or carries scopes with no key id, is refused every key policy.</summary>
    [Fact]
    public async Task A_principal_that_is_not_a_key_is_refused_every_key_policy()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var noKeyId = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(PanelClaimTypes.ApiKeyScope, ApiKeyScopes.PlansRead), new Claim(PanelClaimTypes.ApiKeyReseller, Guid.NewGuid().ToString())],
            "Bearer"));

        foreach (var policy in new[] { ApiKeyPolicyNames.SelfRead, ApiKeyPolicyNames.ServerRead, ApiKeyPolicyNames.PlansRead, ApiKeyPolicyNames.AccountsRead, ApiKeyPolicyNames.AccountsWrite, ApiKeyPolicyNames.AccountsDelete })
        {
            Assert.False(await PassesAsync(anonymous, policy));
            Assert.False(await PassesAsync(noKeyId, policy));
        }
    }
}
