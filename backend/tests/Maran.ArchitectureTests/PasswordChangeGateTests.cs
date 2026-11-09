using Maran.Host.Authorization;
using Maran.Modules.Identity.Authorization;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.ArchitectureTests;

/// <summary>
/// Holds the rule that every policy a signed-in login can satisfy carries the first-password gate, so a
/// login created with an initial password can reach nothing but the password change (issue #194).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a census and not the integration walk alone.</b> The Host integration test proves the gate on
/// the routes that exist today; this one proves it on the POLICIES, so an endpoint added under any of
/// them is gated before it is written. It is the cheaper half and the one that fails first.
/// </para>
/// <para>
/// <b>Controls.</b> The real walk is satisfied by a census that looks at nothing, so the checker is also
/// handed a policy it must name (the planted violation) and a set it must leave alone (the accepting
/// control: a gated policy, an API key policy and a gated fallback and default).
/// </para>
/// </remarks>
public sealed class PasswordChangeGateTests
{
    /// <summary>Configures the options exactly as the Host does.</summary>
    /// <returns>The panel's authorization options.</returns>
    private static AuthorizationOptions PanelOptions()
    {
        var options = new AuthorizationOptions();
        RolePolicies.Configure(options);
        ApiKeyPolicies.Configure(options);
        return options;
    }

    /// <summary>Every login policy, the fallback and the default carry the first-password gate.</summary>
    [Fact]
    public void Every_login_policy_the_fallback_and_the_default_carry_the_gate()
    {
        var violations = PasswordChangeGateCensus.FindUngatedPolicies(
            PanelOptions(),
            PasswordChangeGateCensus.LoginPolicyNames().Concat(PasswordChangeGateCensus.ApiKeyPolicyNameList()));

        Assert.True(
            violations.Count == 0,
            "These policies can be satisfied by a login that still owes its first password change, and do not "
            + "refuse it: " + string.Join("; ", violations) + ". Add PasswordChangeCompleteRequirement to them.");
    }

    /// <summary>The walk reaches the four login policies and the key policies, and exempts only the latter.</summary>
    /// <remarks>
    /// The vacuity guard on the axis that goes blind: the walk is satisfied by an empty name list, so the
    /// names it reads must include the four the panel defines, and the exemption must be by scheme and
    /// must have something to exempt.
    /// </remarks>
    [Fact]
    public void The_walk_reaches_the_login_policies_and_exempts_only_the_api_key_ones()
    {
        var options = PanelOptions();
        var loginPolicies = PasswordChangeGateCensus.LoginPolicyNames();
        var keyPolicies = PasswordChangeGateCensus.ApiKeyPolicyNameList();

        Assert.Equal(
            new[]
            {
                AuthorizationPolicies.AdminOnly,
                AuthorizationPolicies.AdminOrReseller,
                AuthorizationPolicies.AnyAuthenticated,
                AuthorizationPolicies.ResellerOnly,
            },
            loginPolicies);
        Assert.NotEmpty(keyPolicies);

        foreach (var name in loginPolicies)
        {
            var policy = options.GetPolicy(name)!;
            Assert.False(PasswordChangeGateCensus.AdmitsApiKey(policy), name + " must not be an API key policy");
            Assert.Contains(policy.Requirements, requirement => { return requirement is PasswordChangeCompleteRequirement; });
        }

        foreach (var name in keyPolicies)
        {
            Assert.True(PasswordChangeGateCensus.AdmitsApiKey(options.GetPolicy(name)!), name + " must name the ApiKey scheme");
        }
    }

    /// <summary>The census names a policy that lacks the gate, a policy that is not registered, and a missing fallback.</summary>
    /// <remarks>The planted violation: without it the real walk is satisfied by a checker that finds nothing.</remarks>
    [Fact]
    public void The_census_names_every_way_a_policy_can_lack_the_gate()
    {
        var options = new AuthorizationOptions();
        options.AddPolicy("Planted", policy => { policy.RequireAuthenticatedUser(); });
        options.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

        var violations = PasswordChangeGateCensus.FindUngatedPolicies(options, ["Planted", "Unregistered"]);

        Assert.Equal(
            ["Planted lacks PasswordChangeCompleteRequirement",
                "Unregistered is named but not registered",
                "(fallback policy) is not set, so an endpoint falling back to it is not gated",
                "(default policy) lacks PasswordChangeCompleteRequirement",],
            violations);
    }

    /// <summary>The census says nothing about a gated policy, an API key policy, or a gated fallback and default.</summary>
    /// <remarks>The inverse control: a checker that refuses everything passes every test that only hands it broken input.</remarks>
    [Fact]
    public void The_census_accepts_a_gated_policy_an_api_key_policy_and_a_gated_fallback_and_default()
    {
        var gated = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PasswordChangeCompleteRequirement())
            .Build();
        var options = new AuthorizationOptions();
        options.AddPolicy("Gated", gated);
        options.AddPolicy("Machine", policy =>
        {
            policy.AddAuthenticationSchemes(ApiKeyDefaults.Scheme);
            policy.RequireAuthenticatedUser();
        });
        options.FallbackPolicy = gated;
        options.DefaultPolicy = gated;

        var violations = PasswordChangeGateCensus.FindUngatedPolicies(options, ["Gated", "Machine"]);

        Assert.Empty(violations);
    }
}
