using Maran.ArchitectureTests.Fixtures;
using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Guard.Enums;

namespace Maran.ArchitectureTests;

/// <summary>
/// Holds the rule that the public demo's refusal table names every mutating action of every composed
/// controller, so that what a visitor may do is a reviewed decision and not a silence (issue #95).
/// </summary>
/// <remarks>
/// Every refusal has a planted control and an accept-side control: the real walk is satisfied by an empty
/// answer, so the checker is also handed <see cref="UnclassifiedDemoController"/> (each failure mode, each of
/// which it must name, and a read it must leave alone) and <see cref="ClassifiedDemoController"/> (every
/// legitimate form, about which it must say nothing).
/// </remarks>
public sealed class DemoRefusalCensusTests
{
    /// <summary>The actions the planted table names, one of them with a route value its route lacks.</summary>
    private static readonly DemoEndpointPolicy PlantedPolicy = new(
        new Dictionary<string, DemoEndpointRule>(StringComparer.Ordinal)
        {
            ["UnclassifiedDemoController.WrongKeyAsync"] = DemoEndpointRule.AllowUnlessProtected("id"),
            ["UnclassifiedDemoController.AnonymousCountedAsync"] = DemoEndpointRule.Allow(),
            ["ClassifiedDemoController.RefusedAsync"] = DemoEndpointRule.Refuse(DemoRefusalArea.Smtp),
            ["ClassifiedDemoController.ProtectedCheckAsync"] = DemoEndpointRule.AllowUnlessProtected("id"),
            ["ClassifiedDemoController.AnonymousUncountedAsync"] = DemoEndpointRule.AllowUncounted(),
            ["ClassifiedDemoController.AllowedAsync"] = DemoEndpointRule.Allow(),
            ["GoneController.DeleteAsync"] = DemoEndpointRule.Allow(),
        });

    /// <summary>Every mutating action of every composed (and every shipped) controller is classified and consistent.</summary>
    [Fact]
    public void Every_mutating_composed_action_is_classified()
    {
        var controllers = ControllerAuthorizationCensus.ComposedControllers()
            .Concat(ControllerAuthorizationCensus.ShippedControllers())
            .Distinct()
            .ToList();

        var violations = DemoRefusalCensus.FindViolations(controllers, DemoEndpointPolicy.Default);

        Assert.True(
            violations.Count == 0,
            "The public demo does not know what to do with these actions: "
            + string.Join("; ", violations)
            + ". Add each to Maran.Host.Demo/Guard/DemoEndpointTable.cs: Refuse(area) for anything that creates or "
            + "alters a credential, session, mail, licence, certificate, policy or key; Allow() for a resource "
            + "change against the simulated agent.");
    }

    /// <summary>Every table entry still names a mutating action.</summary>
    [Fact]
    public void The_table_names_only_mutating_actions_that_exist()
    {
        var stale = DemoRefusalCensus.FindStaleEntries(
            ControllerAuthorizationCensus.ComposedControllers(),
            DemoEndpointPolicy.Default);

        Assert.True(stale.Count == 0, "DemoEndpointTable has entries that name nothing: " + string.Join("; ", stale));
    }

    /// <summary>The walk is not blind: it sees a hundred or more actions and the table uses every access.</summary>
    /// <remarks>The vacuity guard on the axis that can go blind: the walk is satisfied by finding no action at all.</remarks>
    [Fact]
    public void The_walk_reaches_the_mutating_actions_and_the_table_uses_every_kind_of_access()
    {
        var mutating = ControllerAuthorizationCensus.ComposedControllers()
            .SelectMany(controller => { return ControllerAuthorizationCensus.ActionsOf(controller); })
            .Count(DemoRefusalCensus.IsMutation);
        var accesses = DemoEndpointTable.Rules.Values.Select(rule => { return rule.Access; }).Distinct().ToList();

        Assert.True(mutating >= 100, $"Only {mutating} mutating actions were found.");
        Assert.Contains(DemoAccess.Allowed, accesses);
        Assert.Contains(DemoAccess.AllowedUnlessProtected, accesses);
        Assert.Contains(DemoAccess.Refused, accesses);
    }

    /// <summary>The id the table is keyed by is the id the census names actions by.</summary>
    [Fact]
    public void The_run_time_action_id_is_the_census_name()
    {
        var action = ControllerAuthorizationCensus.ActionsOf(typeof(ClassifiedDemoController)).First();

        Assert.Equal(ControllerAuthorizationCensus.NameOf(action), DemoEndpointPolicy.ActionIdOf(action));
    }

    /// <summary>The census names every way a mutating action can be left unclassified or misclassified, and ignores a read.</summary>
    /// <remarks>The planted-violation control: without it the real walk is satisfied by a checker that finds nothing.</remarks>
    [Fact]
    public void The_census_names_every_way_an_action_can_be_misclassified()
    {
        var violations = DemoRefusalCensus.FindViolations(new[] { typeof(UnclassifiedDemoController) }, PlantedPolicy);

        Assert.Equal(
            ["UnclassifiedDemoController.AnonymousCountedAsync is anonymous and allowed but would spend the shared budget",
                "UnclassifiedDemoController.MissingAsync is not classified in DemoEndpointTable",
                "UnclassifiedDemoController.WrongKeyAsync checks route value 'id' that its route does not carry",],
            violations.Order(StringComparer.Ordinal));
    }

    /// <summary>The census says nothing about every legitimate classification.</summary>
    /// <remarks>The inverse control: a checker that refuses everything passes every test above.</remarks>
    [Fact]
    public void The_census_accepts_every_legitimate_classification()
    {
        Assert.Empty(DemoRefusalCensus.FindViolations(new[] { typeof(ClassifiedDemoController) }, PlantedPolicy));
    }

    /// <summary>An entry that names no mutating action is reported, and an accurate table is not.</summary>
    [Fact]
    public void The_stale_check_reports_a_ghost_entry_and_accepts_an_accurate_table()
    {
        var stale = DemoRefusalCensus.FindStaleEntries(
            new[] { typeof(ClassifiedDemoController), typeof(UnclassifiedDemoController) },
            PlantedPolicy);

        Assert.Equal(["GoneController.DeleteAsync names no mutating action"], stale);
    }
}
