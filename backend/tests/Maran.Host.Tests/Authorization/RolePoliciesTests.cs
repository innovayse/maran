using System.Security.Claims;
using Maran.Host.Authorization;
using Maran.Modules.Identity.Authorization;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maran.Host.Tests.Authorization;

/// <summary>
/// The authorization policies the booted host really carries: the FALLBACK an endpoint without
/// authorization metadata is judged by, and the DEFAULT a bare <c>[Authorize]</c> is judged by.
/// </summary>
/// <remarks>
/// Everything here is read off the composed host rather than off a freshly configured
/// <see cref="AuthorizationOptions"/>, because the guarantee is about what the running server
/// evaluates — a test of <see cref="RolePolicies.Configure"/> alone would stay green if the host
/// stopped registering it. Each assertion is paired with a control on the other side of the line (an
/// administrator IS admitted), so a fallback that refuses everyone cannot pass for a strict one.
/// The route-table half of the guarantee, that no panel endpoint relies on the fallback, is held
/// up by the endpoint census in Maran.Host.IntegrationTests.
/// </remarks>
public sealed class RolePoliciesTests : IClassFixture<PanelTestFactory>
{
    /// <summary>The booted host whose authorization setup is under test.</summary>
    private readonly PanelTestFactory _factory;

    /// <summary>Captures the booted host.</summary>
    /// <param name="factory">The hermetic host factory.</param>
    public RolePoliciesTests(PanelTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>The host's fallback policy is the registered administrator policy itself.</summary>
    [Fact]
    public void The_fallback_policy_is_the_administrator_policy()
    {
        var options = ResolveOptions();

        var administrator = options.GetPolicy(RolePolicies.AdminOnly);

        // NotNull first: with no registration at all both sides are null and Same would pass.
        Assert.NotNull(administrator);
        Assert.Same(administrator, options.FallbackPolicy);
    }

    /// <summary>A signed-in customer is refused by the fallback.</summary>
    /// <returns>Completes once the assertion has run.</returns>
    [Fact]
    public async Task The_fallback_policy_refuses_a_signed_in_customer()
    {
        var result = await EvaluateFallbackAsync(SignedIn(nameof(UserRole.Customer)));

        Assert.False(result.Succeeded);
    }

    /// <summary>An anonymous caller is refused by the fallback.</summary>
    /// <returns>Completes once the assertion has run.</returns>
    [Fact]
    public async Task The_fallback_policy_refuses_an_anonymous_caller()
    {
        var result = await EvaluateFallbackAsync(new ClaimsPrincipal(new ClaimsIdentity()));

        Assert.False(result.Succeeded);
    }

    /// <summary>An administrator is admitted by the fallback — the control that it is not deny-all.</summary>
    /// <returns>Completes once the assertion has run.</returns>
    [Fact]
    public async Task The_fallback_policy_admits_an_administrator()
    {
        var result = await EvaluateFallbackAsync(SignedIn(nameof(UserRole.Admin)));

        Assert.True(result.Succeeded);
    }

    /// <summary>An administrator who still has to enrol in two-factor is refused by the fallback.</summary>
    /// <returns>Completes once the assertion has run.</returns>
    [Fact]
    public async Task The_fallback_policy_refuses_an_administrator_who_must_still_enrol()
    {
        var result = await EvaluateFallbackAsync(SignedIn(nameof(UserRole.Admin), steeredIntoEnrolment: true));

        Assert.False(result.Succeeded);
    }

    /// <summary>The default policy a bare <c>[Authorize]</c> evaluates is the customer-level policy.</summary>
    [Fact]
    public void The_default_policy_is_the_customer_level_policy()
    {
        var options = ResolveOptions();

        var customerLevel = options.GetPolicy(RolePolicies.AnyAuthenticated);

        Assert.NotNull(customerLevel);
        Assert.Same(customerLevel, options.DefaultPolicy);
    }

    /// <summary>A signed-in customer passes the default policy.</summary>
    /// <returns>Completes once the assertion has run.</returns>
    [Fact]
    public async Task The_default_policy_admits_a_signed_in_customer()
    {
        var provider = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await provider.GetDefaultPolicyAsync();

        var result = await EvaluateAsync(policy, SignedIn(nameof(UserRole.Customer)));

        Assert.True(result.Succeeded);
    }

    /// <summary>The default policy still carries the two-factor enrolment requirement.</summary>
    [Fact]
    public void The_default_policy_still_carries_the_two_factor_enrolment_requirement()
    {
        var options = ResolveOptions();

        Assert.Contains(options.DefaultPolicy.Requirements, requirement =>
        {
            return requirement is TwoFactorEnrolmentCompleteRequirement;
        });
    }

    /// <summary>The administrator policy carries the two-factor enrolment requirement.</summary>
    [Fact]
    public void The_administrator_policy_carries_the_two_factor_enrolment_requirement()
    {
        var administrator = ResolveOptions().GetPolicy(RolePolicies.AdminOnly);

        Assert.NotNull(administrator);
        Assert.Contains(administrator.Requirements, requirement =>
        {
            return requirement is TwoFactorEnrolmentCompleteRequirement;
        });
    }

    /// <summary>The host's authorization options, as the running server resolves them.</summary>
    /// <returns>The options carrying the registered policies.</returns>
    private AuthorizationOptions ResolveOptions()
    {
        return _factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
    }

    /// <summary>Builds an authenticated principal holding a role, optionally steered into two-factor enrolment.</summary>
    /// <param name="role">The role claim value.</param>
    /// <param name="steeredIntoEnrolment">Whether the token carries the enrolment-required claim.</param>
    /// <returns>The principal.</returns>
    private static ClaimsPrincipal SignedIn(string role, bool steeredIntoEnrolment = false)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(PanelClaimTypes.Role, role));
        if (steeredIntoEnrolment)
        {
            identity.AddClaim(new Claim(PanelClaimTypes.TwoFactorSetupRequired, "true"));
        }

        return new ClaimsPrincipal(identity);
    }

    /// <summary>Evaluates the host's fallback policy for a caller, with no resource.</summary>
    /// <param name="user">The caller.</param>
    /// <returns>The authorization outcome.</returns>
    private async Task<AuthorizationResult> EvaluateFallbackAsync(ClaimsPrincipal user)
    {
        var provider = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await provider.GetFallbackPolicyAsync();

        // The fallback must exist before it can be evaluated: a null one would be "no fallback",
        // which the framework reads as open.
        Assert.NotNull(policy);

        return await EvaluateAsync(policy, user);
    }

    /// <summary>Evaluates a policy for a caller through the host's real authorization service.</summary>
    /// <param name="policy">The policy to evaluate.</param>
    /// <param name="user">The caller.</param>
    /// <returns>The authorization outcome.</returns>
    private async Task<AuthorizationResult> EvaluateAsync(AuthorizationPolicy policy, ClaimsPrincipal user)
    {
        var service = _factory.Services.GetRequiredService<IAuthorizationService>();

        return await service.AuthorizeAsync(user, null, policy);
    }
}
