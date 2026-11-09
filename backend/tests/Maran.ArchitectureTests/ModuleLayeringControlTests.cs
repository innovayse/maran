using Maran.ArchitectureTests.Fixtures.Layering.Clean.Controllers;
using Maran.ArchitectureTests.Fixtures.Layering.Clean.Domain;
using Maran.ArchitectureTests.Fixtures.Layering.Planted.Controllers;
using Maran.ArchitectureTests.Fixtures.Layering.Planted.Domain;

namespace Maran.ArchitectureTests;

/// <summary>
/// Proves <see cref="ModuleLayerLaw"/> can see what it reports on. Every real module passes the layering
/// tests, so on their own those tests are satisfied by a law that looks at nothing; these feed the law types
/// planted to break it, and types built to satisfy it, through the same two methods.
/// </summary>
public sealed class ModuleLayeringControlTests
{
    /// <summary>The root namespace of the planted fixtures, laid out like a module.</summary>
    private const string PlantedRoot = "Maran.ArchitectureTests.Fixtures.Layering.Planted";

    /// <summary>The root namespace of the clean fixtures, laid out like a module.</summary>
    private const string CleanRoot = "Maran.ArchitectureTests.Fixtures.Layering.Clean";

    /// <summary>
    /// Planted Domain types that reach persistence, a command, a query, a controller and EF Core are each named.
    /// </summary>
    [Fact]
    public void Planted_domain_types_reaching_forbidden_layers_are_each_reported()
    {
        var reported = Offenders(Planted(ModuleLayerLaw.DomainRule));

        // Exact equality, so the stand-in targets are proved NOT flagged either.
        Assert.Equal(
            new[]
            {
                typeof(PlantedDomainReachingCommands).FullName!,
                typeof(PlantedDomainReachingController).FullName!,
                typeof(PlantedDomainReachingEfCore).FullName!,
                typeof(PlantedDomainReachingPersistence).FullName!,
                typeof(PlantedDomainReachingQueries).FullName!,
            },
            reported);
    }

    /// <summary>A planted controller that reaches persistence is named, and the stand-in controller is not.</summary>
    [Fact]
    public void Planted_controller_reaching_persistence_is_reported()
    {
        var reported = Offenders(Planted(ModuleLayerLaw.ControllersRule));

        Assert.Equal(new[] { typeof(PlantedControllerReachingPersistence).FullName! }, reported);
    }

    /// <summary>The violation text names the layer and what was reached, so a failure says what to fix.</summary>
    [Fact]
    public void A_violation_line_names_the_type_the_layer_and_the_forbidden_namespace()
    {
        var lines = Planted(ModuleLayerLaw.DomainRule);

        Assert.Contains(
            $"{typeof(PlantedDomainReachingEfCore).FullName} (Domain) depends on Microsoft.EntityFrameworkCore",
            lines);
    }

    /// <summary>Clean types depending only on allowed layers are accepted by both rules, and were examined.</summary>
    [Fact]
    public void Clean_layers_are_accepted_by_both_rules_and_were_examined()
    {
        var assembly = typeof(CleanController).Assembly;

        Assert.Empty(ModuleLayerLaw.Violations(assembly, CleanRoot, ModuleLayerLaw.DomainRule));
        Assert.Empty(ModuleLayerLaw.Violations(assembly, CleanRoot, ModuleLayerLaw.ControllersRule));
        Assert.Equal(2, ModuleLayerLaw.LayerTypeCount(assembly, CleanRoot, ModuleLayerLaw.DomainRule));
        Assert.Equal(1, ModuleLayerLaw.LayerTypeCount(assembly, CleanRoot, ModuleLayerLaw.ControllersRule));
        Assert.Equal($"{CleanRoot}.Domain", typeof(CleanDomainEntity).Namespace);
    }

    /// <summary>Applies a rule to the planted fixtures.</summary>
    /// <param name="rule">The rule to apply.</param>
    /// <returns>The violation lines the law reports for the planted module.</returns>
    private static List<string> Planted(ModuleLayerRule rule)
    {
        return ModuleLayerLaw.Violations(typeof(PlantedControllerReachingPersistence).Assembly, PlantedRoot, rule);
    }

    /// <summary>The distinct, ordered type names out of violation lines.</summary>
    /// <param name="lines">Lines of the form <c>"type (layer) depends on target"</c>.</param>
    /// <returns>The offending type names, once each.</returns>
    private static string[] Offenders(IEnumerable<string> lines)
    {
        return lines
            .Select(line =>
            {
                return line[..line.IndexOf(" (", StringComparison.Ordinal)];
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
