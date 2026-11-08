using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Guard.Enums;

namespace Maran.Host.Demo.Tests.Guard;

/// <summary>The rule's factories can only build a consistent rule.</summary>
public sealed class DemoEndpointRuleTests
{
    /// <summary>A refusal carries its area, spends nothing and checks no resource.</summary>
    [Fact]
    public void A_refusal_carries_its_area_and_nothing_else()
    {
        var rule = DemoEndpointRule.Refuse(DemoRefusalArea.Licence);

        Assert.Equal(DemoAccess.Refused, rule.Access);
        Assert.Equal(DemoRefusalArea.Licence, rule.Area);
        Assert.Null(rule.ProtectedRouteKey);
        Assert.False(rule.CountsTowardBudget);
    }

    /// <summary>An allowance has no area; the plain one counts and the uncounted one does not.</summary>
    [Fact]
    public void An_allowance_has_no_area_and_counts_unless_it_says_otherwise()
    {
        Assert.Null(DemoEndpointRule.Allow().Area);
        Assert.True(DemoEndpointRule.Allow().CountsTowardBudget);
        Assert.False(DemoEndpointRule.AllowUncounted().CountsTowardBudget);
        Assert.Equal(DemoAccess.Allowed, DemoEndpointRule.AllowUncounted().Access);
    }

    /// <summary>A sign-in allowance draws on the sign-in allowances and on nothing else.</summary>
    [Fact]
    public void A_sign_in_allowance_counts_only_toward_the_sign_in_budget()
    {
        var rule = DemoEndpointRule.AllowSignIn();

        Assert.Equal(DemoAccess.Allowed, rule.Access);
        Assert.True(rule.CountsTowardSignInBudget);
        Assert.False(rule.CountsTowardBudget);
        Assert.False(DemoEndpointRule.Allow().CountsTowardSignInBudget);
        Assert.False(DemoEndpointRule.AllowUncounted().CountsTowardSignInBudget);
    }

    /// <summary>A protected-resource allowance names the route value it checks.</summary>
    [Fact]
    public void A_protected_check_names_its_route_value()
    {
        var rule = DemoEndpointRule.AllowUnlessProtected("id");

        Assert.Equal(DemoAccess.AllowedUnlessProtected, rule.Access);
        Assert.Equal("id", rule.ProtectedRouteKey);
        Assert.True(rule.CountsTowardBudget);
    }

    /// <summary>A protected check with no route value is refused at construction.</summary>
    [Fact]
    public void A_protected_check_without_a_route_value_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => { return DemoEndpointRule.AllowUnlessProtected(" "); });
    }
}
