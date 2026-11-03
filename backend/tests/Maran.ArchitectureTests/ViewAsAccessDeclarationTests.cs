using Maran.ArchitectureTests.Fixtures;
using Maran.Sdk.Contracts;

namespace Maran.ArchitectureTests;

/// <summary>
/// Holds the rule that every action of every composed controller a view-as token can reach says what a
/// view-as token may do there, so that the allow-list's refusals are reviewed decisions and not
/// silences (issue #334).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a census as well as a default.</b> An undeclared endpoint is refused at run time, so the
/// default is safe; the census makes it loud. An author who adds an endpoint is told, in CI, to write
/// down whether an operator viewing as a customer may use it, and a marketplace module's controller —
/// code this repository never tests — is caught the same way when it is composed here.
/// </para>
/// <para>
/// <b>Every refusal has a planted control and an accept-side control.</b> The real walk is satisfied by
/// an empty answer, so the checker is also handed <see cref="UndeclaredViewAsController"/> (one action
/// per failure mode, each of which it must name, and two it must leave alone) and
/// <see cref="DeclaredViewAsController"/> (every legitimate form, about which it must say nothing).
/// </para>
/// <para>
/// UNOBSERVED HERE: minimal endpoints. The Host integration tests read the real endpoint data source.
/// </para>
/// </remarks>
public sealed class ViewAsAccessDeclarationTests
{
    /// <summary>The two violations the reseller-kind controller must be named for.</summary>
    private static readonly string[] ResellerViewAsViolations =
    {
        "UndeclaredResellerViewAsController.OwnAsync declares no ViewAsAccess",
        "UndeclaredResellerViewAsController.SharedAsync declares no ViewAsAccess",
    };

    /// <summary>Every composed action a view-as token can reach declares its access, and declares it consistently.</summary>
    [Fact]
    public void Every_composed_action_a_view_as_token_can_reach_is_classified()
    {
        var violations = ViewAsAccessCensus.FindViolations(
            ControllerAuthorizationCensus.ComposedControllers(),
            ViewAsSafeMutationPin.All);

        Assert.True(
            violations.Count == 0,
            "These actions do not say what a view-as token may do there: "
            + string.Join("; ", violations)
            + ". Give the action [ViewAsAccess(ViewAsAccess.Read)] on a GET, [ViewAsAccess(ViewAsAccess.Refused)], "
            + "or - for a reviewed safe change - [ViewAsAccess(ViewAsAccess.SafeMutation)] and an entry in "
            + "ViewAsSafeMutationPin with its reason.");
    }

    /// <summary>Every pin entry still names a safe-mutation action and carries a reason.</summary>
    [Fact]
    public void The_safe_mutation_pin_describes_only_safe_mutations()
    {
        var stale = ViewAsAccessCensus.FindStalePins(
            ControllerAuthorizationCensus.ComposedControllers(),
            ViewAsSafeMutationPin.All);

        Assert.True(
            stale.Count == 0,
            "ViewAsSafeMutationPin has entries that no longer describe a safe mutation: " + string.Join("; ", stale));
    }

    /// <summary>The census asks about actions at all: the walk is not blind and the pin is not empty.</summary>
    /// <remarks>
    /// The vacuity guard on the axis that can go blind: the walk is satisfied by a census that finds no
    /// reachable action, so it must find the reads the customer's screens depend on and every pinned mutation.
    /// </remarks>
    [Fact]
    public void The_walk_reaches_declared_reads_and_every_pinned_mutation()
    {
        var controllers = ControllerAuthorizationCensus.ComposedControllers();
        var declared = controllers
            .SelectMany(controller =>
            {
                return ControllerAuthorizationCensus.ActionsOf(controller)
                    .Where(action => { return ViewAsAccessCensus.IsReachableByViewAsToken(controller, action); })
                    .Select(action => { return ViewAsAccessCensus.DeclaredAccess(controller, action); });
            })
            .OfType<ViewAsAccess>()
            .ToList();

        Assert.Contains(ViewAsAccess.Read, declared);
        Assert.Contains(ViewAsAccess.Refused, declared);
        Assert.Contains(ViewAsAccess.SafeMutation, declared);
        Assert.NotEmpty(ViewAsSafeMutationPin.All);
    }

    /// <summary>The census names every way an action can fail to be classified, and leaves unreachable ones alone.</summary>
    /// <remarks>The positive control: without it the real walk is satisfied by a checker that finds nothing.</remarks>
    [Fact]
    public void The_census_names_every_way_an_action_can_fail_to_be_classified()
    {
        var violations = ViewAsAccessCensus.FindViolations(
            new[] { typeof(UndeclaredViewAsController) },
            Array.Empty<PinnedSafeMutation>());

        Assert.Equal(
            new[]
            {
                "UndeclaredViewAsController.MutationOnAGetAsync is declared a SafeMutation but only reads; "
                    + "declare it a Read",
                "UndeclaredViewAsController.NoDeclarationAsync declares no ViewAsAccess",
                "UndeclaredViewAsController.ReadOnAPostAsync is declared a Read but answers a method that changes things",
                "UndeclaredViewAsController.UnpinnedMutationAsync is a SafeMutation but is not in ViewAsSafeMutationPin",
            },
            violations);
    }

    /// <summary>The census asks about reseller-policy actions, which a reseller-kind view-as token reaches.</summary>
    /// <remarks>
    /// The refusing control for the reseller edge of issue #317: an administrator viewing as a reseller
    /// holds the reseller role, so an action naming the reseller-only or the admin-or-reseller policy is
    /// reachable and must declare itself; only the administrator-only action is left alone.
    /// </remarks>
    [Fact]
    public void The_census_asks_about_actions_a_reseller_kind_view_as_token_reaches()
    {
        var violations = ViewAsAccessCensus.FindViolations(
            new[] { typeof(UndeclaredResellerViewAsController) },
            Array.Empty<PinnedSafeMutation>());

        Assert.Equal(
            ResellerViewAsViolations,
            violations);
    }

    /// <summary>The census says nothing about every legitimate way of classifying an action.</summary>
    /// <remarks>The inverse control: a checker that refuses everything passes every test above.</remarks>
    [Fact]
    public void The_census_accepts_every_legitimate_classification()
    {
        var pin = new[] { new PinnedSafeMutation("DeclaredViewAsController.PinnedMutationAsync", "planted control") };

        var violations = ViewAsAccessCensus.FindViolations(new[] { typeof(DeclaredViewAsController) }, pin);

        Assert.Empty(violations);
    }

    /// <summary>A pin entry that names nothing, names a non-mutation, or has no reason is reported.</summary>
    [Fact]
    public void The_pin_check_reports_a_stale_entry()
    {
        var controllers = new[] { typeof(DeclaredViewAsController) };
        var pin = new[]
        {
            new PinnedSafeMutation("DeclaredViewAsController.PinnedMutationAsync", "planted control"),
            new PinnedSafeMutation("DeclaredViewAsController.NowhereAsync", "names no action"),
            new PinnedSafeMutation("DeclaredViewAsController.ReadAsync", "is not a mutation"),
            new PinnedSafeMutation("DeclaredViewAsController.PinnedMutationAsync", " "),
        };

        var stale = ViewAsAccessCensus.FindStalePins(controllers, pin);

        Assert.Equal(
            new List<string>
            {
                "DeclaredViewAsController.NowhereAsync names no action",
                "DeclaredViewAsController.ReadAsync is pinned as a safe mutation but is not declared one",
                "DeclaredViewAsController.PinnedMutationAsync has no reason",
            },
            stale);
    }

    /// <summary>A pin that is entirely accurate produces no complaint.</summary>
    /// <remarks>The inverse control of <see cref="The_pin_check_reports_a_stale_entry"/>.</remarks>
    [Fact]
    public void The_pin_check_accepts_an_accurate_entry()
    {
        var pin = new[] { new PinnedSafeMutation("DeclaredViewAsController.PinnedMutationAsync", "planted control") };

        var stale = ViewAsAccessCensus.FindStalePins(new[] { typeof(DeclaredViewAsController) }, pin);

        Assert.Empty(stale);
    }
}
