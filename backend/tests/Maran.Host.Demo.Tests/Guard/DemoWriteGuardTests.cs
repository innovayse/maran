using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Guard.Enums;
using Maran.Host.Demo.Tests.Fixtures;
using Maran.Modules.Identity.Controllers;
using Microsoft.AspNetCore.Routing;

namespace Maran.Host.Demo.Tests.Guard;

/// <summary>What the guard decides for each kind of request.</summary>
public sealed class DemoWriteGuardTests
{
    /// <summary>The id the protected-resource tests use.</summary>
    private static readonly Guid SeededId = new("00000000-0000-4000-8000-0000000000a1");

    /// <summary>A guard over the planted table and a protected set holding <see cref="SeededId"/>.</summary>
    /// <returns>The guard.</returns>
    private static DemoWriteGuard PlantedGuard()
    {
        var protectedResources = new DemoProtectedResources();
        protectedResources.Protect(SeededId);
        protectedResources.MarkLoaded();
        return new DemoWriteGuard(DemoEndpoints.PlantedPolicy, protectedResources);
    }

    /// <summary>Route values holding one id.</summary>
    /// <param name="value">The value of <c>id</c>.</param>
    /// <returns>The route values.</returns>
    private static RouteValueDictionary Route(string value)
    {
        return new RouteValueDictionary { ["id"] = value };
    }

    /// <summary>A read is never judged, whatever the table says about the action.</summary>
    [Fact]
    public void A_read_passes_without_being_counted()
    {
        var endpoint = DemoEndpoints.ForAction(
            typeof(PlantedDemoController),
            nameof(PlantedDemoController.RefusedAsync));

        var decision = PlantedGuard().Decide("GET", endpoint, new RouteValueDictionary());

        Assert.Equal(DemoGuardDecision.PassUncounted, decision);
    }

    /// <summary>A request that matched no endpoint runs nothing and is not judged.</summary>
    [Fact]
    public void A_request_with_no_endpoint_passes()
    {
        Assert.True(PlantedGuard().Decide("POST", null, new RouteValueDictionary()).Permitted);
    }

    /// <summary>A classified refusal is refused with its own area.</summary>
    [Fact]
    public void A_classified_refusal_is_refused_with_its_area()
    {
        var endpoint = DemoEndpoints.ForAction(
            typeof(PlantedDemoController),
            nameof(PlantedDemoController.RefusedAsync));

        var decision = PlantedGuard().Decide("POST", endpoint, new RouteValueDictionary());

        Assert.False(decision.Permitted);
        Assert.Equal(DemoRefusalArea.Smtp, decision.Area);
    }

    /// <summary>The fail-closed default: a mutating action nobody classified is refused as unclassified.</summary>
    [Fact]
    public void A_mutating_action_nobody_classified_is_refused()
    {
        var endpoint = DemoEndpoints.ForAction(
            typeof(PlantedDemoController),
            nameof(PlantedDemoController.UnnamedAsync));

        var decision = PlantedGuard().Decide("POST", endpoint, new RouteValueDictionary());

        Assert.False(decision.Permitted);
        Assert.Equal(DemoRefusalArea.Unclassified, decision.Area);
    }

    /// <summary>The inverse control: allowed actions pass, counted or not as their rule says.</summary>
    [Fact]
    public void An_allowed_action_passes_and_is_counted_as_its_rule_says()
    {
        var counted = DemoEndpoints.ForAction(
            typeof(PlantedDemoController),
            nameof(PlantedDemoController.CountedAsync));
        var open = DemoEndpoints.ForAction(typeof(PlantedDemoController), nameof(PlantedDemoController.OpenAsync));

        Assert.Equal(DemoGuardDecision.PassCounted, PlantedGuard().Decide("POST", counted, new RouteValueDictionary()));
        Assert.Equal(DemoGuardDecision.PassUncounted, PlantedGuard().Decide("POST", open, new RouteValueDictionary()));
    }

    /// <summary>A protected resource is refused; another resource through the same action is not.</summary>
    [Fact]
    public void A_protected_resource_is_refused_and_another_one_is_not()
    {
        var endpoint = DemoEndpoints.ForAction(
            typeof(PlantedDemoController),
            nameof(PlantedDemoController.DeleteAsync));

        var refused = PlantedGuard().Decide("DELETE", endpoint, Route(SeededId.ToString()));
        var allowed = PlantedGuard().Decide("DELETE", endpoint, Route(Guid.NewGuid().ToString()));

        Assert.Equal(DemoRefusalArea.ProtectedResource, refused.Area);
        Assert.Equal(DemoGuardDecision.PassCounted, allowed);
    }

    /// <summary>
    /// Until the protected set is loaded the guard cannot tell a protected resource from another, so it refuses
    /// the action for any id (the empty set it would otherwise consult allows everything).
    /// </summary>
    [Fact]
    public void An_action_that_depends_on_the_protected_set_is_refused_while_the_set_is_not_loaded()
    {
        var endpoint = DemoEndpoints.ForAction(
            typeof(PlantedDemoController),
            nameof(PlantedDemoController.DeleteAsync));
        var guard = new DemoWriteGuard(DemoEndpoints.PlantedPolicy, new DemoProtectedResources());

        var decision = guard.Decide("DELETE", endpoint, Route(Guid.NewGuid().ToString()));

        Assert.Equal(DemoRefusalArea.ProtectedResource, decision.Area);
    }

    /// <summary>The control: an action that does not depend on the set is unaffected by its not being loaded.</summary>
    [Fact]
    public void An_action_that_does_not_depend_on_the_protected_set_passes_while_it_is_not_loaded()
    {
        var endpoint = DemoEndpoints.ForAction(
            typeof(PlantedDemoController),
            nameof(PlantedDemoController.CountedAsync));
        var guard = new DemoWriteGuard(DemoEndpoints.PlantedPolicy, new DemoProtectedResources());

        Assert.Equal(DemoGuardDecision.PassCounted, guard.Decide("POST", endpoint, new RouteValueDictionary()));
    }

    /// <summary>A route value that is not an id is not judged; the action itself will refuse it.</summary>
    [Fact]
    public void A_route_value_that_is_not_an_id_is_not_judged()
    {
        var endpoint = DemoEndpoints.ForAction(
            typeof(PlantedDemoController),
            nameof(PlantedDemoController.DeleteAsync));

        Assert.True(PlantedGuard().Decide("DELETE", endpoint, Route("not-a-guid")).Permitted);
    }

    /// <summary>The one named minimal endpoint passes uncounted.</summary>
    [Fact]
    public void The_search_endpoint_passes_uncounted()
    {
        var decision = PlantedGuard().Decide(
            "POST",
            DemoEndpoints.Minimal("/api/v1/search"),
            new RouteValueDictionary());

        Assert.Equal(DemoGuardDecision.PassUncounted, decision);
    }

    /// <summary>Any other mutating minimal endpoint is closed until it is named.</summary>
    [Fact]
    public void Any_other_mutating_minimal_endpoint_is_refused()
    {
        var decision = PlantedGuard().Decide(
            "POST",
            DemoEndpoints.Minimal("/api/v1/anything"),
            new RouteValueDictionary());

        Assert.Equal(DemoRefusalArea.Unclassified, decision.Area);
    }

    /// <summary>The real table refuses a password change and lets sign-in through on the sign-in allowances.</summary>
    [Fact]
    public void The_real_table_refuses_a_password_change_and_counts_sign_in_on_its_own_allowances()
    {
        var guard = new DemoWriteGuard(DemoEndpointPolicy.Default, new DemoProtectedResources());
        var change = DemoEndpoints.ForAction(typeof(AuthController), nameof(AuthController.ChangePasswordAsync));
        var login = DemoEndpoints.ForAction(typeof(AuthController), nameof(AuthController.LoginAsync));

        Assert.Equal(DemoRefusalArea.Credentials, guard.Decide("POST", change, new RouteValueDictionary()).Area);
        Assert.Equal(DemoGuardDecision.PassSignIn, guard.Decide("POST", login, new RouteValueDictionary()));
    }

    /// <summary>
    /// The journal label of a controller endpoint is its id; of a minimal one, its method and pattern.
    /// </summary>
    [Fact]
    public void The_label_names_the_endpoint_and_never_a_route_value()
    {
        var endpoint = DemoEndpoints.ForAction(
            typeof(PlantedDemoController),
            nameof(PlantedDemoController.DeleteAsync));

        Assert.Equal("PlantedDemoController.DeleteAsync", DemoWriteGuard.LabelOf("DELETE", endpoint));
        Assert.Equal("POST /api/v1/search", DemoWriteGuard.LabelOf("POST", DemoEndpoints.Minimal("/api/v1/search")));
        Assert.Equal("POST unmatched", DemoWriteGuard.LabelOf("POST", null));
    }
}
