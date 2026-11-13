using Maran.ArchitectureTests.Fixtures;
using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Guard.Enums;

namespace Maran.ArchitectureTests;

/// <summary>
/// Holds the rule that no screened action of the composed panel takes text deeper than the public demo's walk
/// reads, because the walk refuses what it cannot read (issue #560).
/// </summary>
/// <remarks>
/// Every refusal has a planted control and an accept-side control: the real walk is satisfied by an empty answer,
/// so the checker is also handed <see cref="DemoTextPlantedController"/>.
/// </remarks>
public sealed class DemoFreeTextCensusTests
{
    /// <summary>The planted table: the deep, recursive and shallow mutations are allowed (and so screened), one is refused.</summary>
    private static readonly DemoEndpointPolicy PlantedPolicy = new(
        new Dictionary<string, DemoEndpointRule>(StringComparer.Ordinal)
        {
            ["DemoTextPlantedController.DeepAsync"] = DemoEndpointRule.Allow(),
            ["DemoTextPlantedController.RecursiveAsync"] = DemoEndpointRule.Allow(),
            ["DemoTextPlantedController.ShallowAsync"] = DemoEndpointRule.Allow(),
            ["DemoTextPlantedController.RefusedAsync"] = DemoEndpointRule.Refuse(DemoRefusalArea.Smtp),
        });

    /// <summary>No screened action of any composed controller takes text deeper than the walk reads.</summary>
    [Fact]
    public void No_screened_action_takes_text_deeper_than_the_walk_reads()
    {
        var controllers = ControllerAuthorizationCensus.ComposedControllers()
            .Concat(ControllerAuthorizationCensus.ShippedControllers())
            .Distinct()
            .ToList();

        var violations = DemoFreeTextCensus.FindViolations(controllers, DemoEndpointPolicy.Default);

        Assert.True(
            violations.Count == 0,
            "The demo's text walk would refuse every write to these actions: "
            + string.Join("; ", violations)
            + $". Flatten the command, or raise DemoFreeText.MaximumDepth ({DemoFreeText.MaximumDepth}) on purpose.");
    }

    /// <summary>The census is not blind: it examines eighty or more screened arguments (82 when this floor was set) that can hold text.</summary>
    /// <remarks>The vacuity guard on the axis that can go blind: the rule is satisfied by finding no argument at all.</remarks>
    [Fact]
    public void The_census_reaches_the_screened_arguments()
    {
        var arguments = DemoFreeTextCensus.ScreenedArguments(
            ControllerAuthorizationCensus.ComposedControllers(),
            DemoEndpointPolicy.Default);

        Assert.True(arguments.Count >= 80, $"Only {arguments.Count} screened arguments were found.");
        Assert.Contains(arguments, argument => { return argument.Depth >= 2; });
    }

    /// <summary>The census names a body nested too deep and one that contains itself.</summary>
    /// <remarks>The planted-violation control: without it the real walk is satisfied by a checker that finds nothing.</remarks>
    [Fact]
    public void The_census_names_a_deep_body_and_a_recursive_one()
    {
        var violations = DemoFreeTextCensus.FindViolations(new[] { typeof(DemoTextPlantedController) }, PlantedPolicy);

        Assert.Equal(
            ["DemoTextPlantedController.DeepAsync parameter 'body' holds text 9 levels deep",
                "DemoTextPlantedController.RecursiveAsync parameter 'body' holds text in a type that contains itself",],
            violations.Order(StringComparer.Ordinal));
    }

    /// <summary>The census says nothing about a shallow body, a read, a refused action or a cancellation token.</summary>
    /// <remarks>The inverse control: a checker that refuses everything passes every test above.</remarks>
    [Fact]
    public void The_census_accepts_shallow_bodies_and_leaves_reads_and_refused_actions_alone()
    {
        var arguments = DemoFreeTextCensus.ScreenedArguments(new[] { typeof(DemoTextPlantedController) }, PlantedPolicy);

        Assert.DoesNotContain(arguments, argument => { return argument.Action.EndsWith("ReadAsync", StringComparison.Ordinal); });
        Assert.DoesNotContain(arguments, argument => { return argument.Action.EndsWith("RefusedAsync", StringComparison.Ordinal); });
        Assert.DoesNotContain(arguments, argument => { return argument.Parameter.Name == "cancellationToken"; });
        Assert.Contains(arguments, argument => { return argument.Action.EndsWith("ShallowAsync", StringComparison.Ordinal) && argument.Parameter.Name == "name" && argument.Depth == 0; });
        Assert.Contains(arguments, argument => { return argument.Action.EndsWith("ShallowAsync", StringComparison.Ordinal) && argument.Parameter.Name == "body" && argument.Depth == 2; });
    }
}
