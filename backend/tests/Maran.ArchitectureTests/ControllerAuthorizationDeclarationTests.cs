using Maran.ArchitectureTests.Fixtures;

namespace Maran.ArchitectureTests;

/// <summary>
/// Holds the rule that every action of every composed controller either is anonymous on purpose or
/// names one of the panel's two policies, so that no action is ever judged by the fallback policy
/// by accident.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a census and not the fallback alone.</b> The fallback is administrator-only, so a
/// forgotten attribute fails closed; but a marketplace module is code this repository never tests,
/// and "it happens to be closed" is not a reviewed decision. This reads every controller of every
/// module the Host composes and refuses the undeclared, the bare, the role-restricted and the
/// misspelled forms, and an anonymous action that is not in <see cref="AnonymousActionPin"/>.
/// </para>
/// <para>
/// <b>Every refusal has a planted control and an accept-side control.</b> The real walk is
/// satisfied by an empty answer, so the checker is also handed
/// <see cref="UndeclaredAuthorizationController"/> (one action per failure mode, each of which it
/// must name) and <see cref="DeclaredAuthorizationController"/> (every legitimate form, about which
/// it must say nothing). The walk itself is held against a second source, the module assemblies
/// that shipped, so a module dropped from the registry turns a test red instead of shrinking the
/// census.
/// </para>
/// <para>
/// UNOBSERVED HERE: minimal endpoints, and a policy applied by a filter or by route-group metadata.
/// The route-table census in Maran.Host.IntegrationTests reads those.
/// </para>
/// </remarks>
public sealed class ControllerAuthorizationDeclarationTests
{
    /// <summary>Every composed action names its policy, or is anonymous and pinned.</summary>
    [Fact]
    public void Every_composed_action_names_its_policy_or_is_pinned_anonymous()
    {
        var violations = ControllerAuthorizationCensus.FindViolations(
            ControllerAuthorizationCensus.ComposedControllers(),
            AnonymousActionPin.All);

        Assert.True(
            violations.Count == 0,
            "These actions do not declare who may call them in a way the panel allows: "
            + string.Join("; ", violations)
            + ". Give the controller or action [Authorize(Policy = AuthorizationPolicies.AdminOnly)] "
            + "or AnyAuthenticated, or - for a deliberate anonymous action - add it to AnonymousActionPin "
            + "with its reason.");
    }

    /// <summary>Every pin entry still names an anonymous action and carries a reason.</summary>
    [Fact]
    public void The_anonymous_pin_describes_only_anonymous_actions()
    {
        var stale = ControllerAuthorizationCensus.FindStalePins(
            ControllerAuthorizationCensus.ComposedControllers(),
            AnonymousActionPin.All);

        Assert.True(
            stale.Count == 0,
            "AnonymousActionPin has entries that no longer describe an anonymous action: "
            + string.Join("; ", stale));
    }

    /// <summary>The walk sees the same controllers as the build ships, and every one yields actions.</summary>
    /// <remarks>
    /// The vacuity guard on the axis that can go blind: the walk reads <c>ModuleRegistry.All</c>, a
    /// hand-written list, so a module left out of it would shrink the census without turning it red.
    /// </remarks>
    [Fact]
    public void The_walk_covers_every_controller_the_build_ships()
    {
        var composed = ControllerAuthorizationCensus.ComposedControllers();
        var shipped = ControllerAuthorizationCensus.ShippedControllers();

        Assert.NotEmpty(shipped);
        Assert.Equal(
            shipped.Select(type => { return type.FullName; }).ToList(),
            composed.Select(type => { return type.FullName; }).ToList());

        var blind = composed
            .Where(type => { return ControllerAuthorizationCensus.ActionsOf(type).Count == 0; })
            .Select(type => { return type.Name; })
            .ToList();
        Assert.True(
            blind.Count == 0,
            $"These controllers yielded no routable action, so the census saw nothing of them: {string.Join(", ", blind)}");
    }

    /// <summary>The census names every way an action can fail to declare its policy.</summary>
    /// <remarks>The positive control: without it the real walk is satisfied by a checker that finds nothing.</remarks>
    [Fact]
    public void The_census_names_every_way_an_action_can_fail_to_declare_its_policy()
    {
        var violations = ControllerAuthorizationCensus.FindViolations(
            new[] { typeof(UndeclaredAuthorizationController) },
            Array.Empty<PinnedAnonymousAction>());

        Assert.Equal(
            new[]
            {
                "UndeclaredAuthorizationController.AnonymousAsync is anonymous but is not in AnonymousActionPin",
                "UndeclaredAuthorizationController.BareAsync uses a bare [Authorize] that names no policy",
                "UndeclaredAuthorizationController.NoAttributeAsync declares no authorization "
                    + "(it would fall to the fallback policy)",
                "UndeclaredAuthorizationController.RolesAsync restricts by Roles or AuthenticationSchemes "
                    + "instead of a named policy",
                "UndeclaredAuthorizationController.UnknownPolicyAsync names the unknown policy 'AdminOnIy'",
            },
            violations);
    }

    /// <summary>The census says nothing about every legitimate way of declaring a policy.</summary>
    /// <remarks>The inverse control: a checker that refuses everything passes every test above.</remarks>
    [Fact]
    public void The_census_accepts_every_legitimate_declaration()
    {
        var pin = new[] { new PinnedAnonymousAction("DeclaredAuthorizationController.OpenAsync", "planted control") };

        var violations = ControllerAuthorizationCensus.FindViolations(
            new[] { typeof(DeclaredAuthorizationController) },
            pin);

        Assert.Empty(violations);
    }

    /// <summary>A pin entry that names nothing, names a protected action, or has no reason is reported.</summary>
    [Fact]
    public void The_pin_check_reports_a_stale_entry()
    {
        var controllers = new[] { typeof(DeclaredAuthorizationController) };
        var pin = new[]
        {
            new PinnedAnonymousAction("DeclaredAuthorizationController.OpenAsync", "planted control"),
            new PinnedAnonymousAction("DeclaredAuthorizationController.NowhereAsync", "names no action"),
            new PinnedAnonymousAction("DeclaredAuthorizationController.AdminAsync", "is not anonymous"),
            new PinnedAnonymousAction("DeclaredAuthorizationController.OpenAsync", " "),
        };

        var stale = ControllerAuthorizationCensus.FindStalePins(controllers, pin);

        Assert.Equal(
            new List<string>
            {
                "DeclaredAuthorizationController.NowhereAsync names no action",
                "DeclaredAuthorizationController.AdminAsync is pinned as anonymous but is not",
                "DeclaredAuthorizationController.OpenAsync has no reason",
            },
            stale);
    }

    /// <summary>A pin that is entirely accurate produces no complaint.</summary>
    /// <remarks>The inverse control of <see cref="The_pin_check_reports_a_stale_entry"/>.</remarks>
    [Fact]
    public void The_pin_check_accepts_an_accurate_entry()
    {
        var pin = new[] { new PinnedAnonymousAction("DeclaredAuthorizationController.OpenAsync", "planted control") };

        var stale = ControllerAuthorizationCensus.FindStalePins(new[] { typeof(DeclaredAuthorizationController) }, pin);

        Assert.Empty(stale);
    }
}
