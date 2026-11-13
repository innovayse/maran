using Maran.ArchitectureTests.Fixtures;

namespace Maran.ArchitectureTests;

/// <summary>
/// Pins the layering inside every module (rules/architecture.md): the Domain depends on no
/// application layer and no EF Core, and a controller never reaches persistence. Each module is
/// judged by <see cref="ModuleLayerLaw"/>, the same code <see cref="ModuleLayeringControlTests"/> feeds
/// planted violations to.
/// </summary>
public sealed class ModuleLayeringTests
{
    /// <summary>What the walk cannot see, printed in every failure so a green run is not over-read.</summary>
    private const string BlindSpot =
        "UNOBSERVED HERE: dependencies through reflection, string type names, or the DI container.";

    /// <summary>
    /// The Domain of every module depends on no persistence, command, query or controller type and on no EF Core.
    /// </summary>
    [Fact]
    public void Domain_depends_on_no_application_layer_and_no_EF_Core()
    {
        AssertLayerHolds(ModuleLayerLaw.DomainRule);
    }

    /// <summary>The controllers of every module do not reach the module's persistence.</summary>
    [Fact]
    public void Controllers_do_not_reach_persistence()
    {
        AssertLayerHolds(ModuleLayerLaw.ControllersRule);
    }

    /// <summary>
    /// Walks every module for one rule and asserts both that it is clean and that the walk looked at something.
    /// </summary>
    /// <param name="rule">The rule to apply to each module.</param>
    private static void AssertLayerHolds(ModuleLayerRule rule)
    {
        var modules = ModuleAssemblies.Modules();
        var violations = new List<string>();
        var examined = 0;
        var modulesWithTypes = 0;

        foreach (var module in modules)
        {
            var root = module.GetName().Name!;
            if (Array.Exists(module.GetTypes(), type =>
            {
                return type.Namespace is not null;
            }))
            {
                modulesWithTypes++;
            }

            examined += ModuleLayerLaw.LayerTypeCount(module, root, rule);
            violations.AddRange(ModuleLayerLaw.Violations(module, root, rule).Select(line =>
            {
                return $"[{root}] {line}";
            }));
        }

        // The vacuity guard sits on the axes that can go blind: the module set, and the layer's types.
        // The layer is summed over modules because Cron, Files, Backups and Licensing own
        // no domain (rules/csharp.md), so a per-module floor would be false.
        Assert.True(modules.Count > 0, $"No module assembly was loaded, so nothing was judged. {BlindSpot}");
        Assert.True(
            modulesWithTypes == modules.Count,
            $"{modulesWithTypes} of {modules.Count} module assemblies declared any namespaced type. {BlindSpot}");
        Assert.True(
            examined > 0,
            $"The {rule.Layer} walk examined {examined} types across {modules.Count} modules, "
            + $"so it proved nothing. {BlindSpot}");
        Assert.True(
            violations.Count == 0,
            $"The {rule.Layer} layer of a module breaks the layering (rules/architecture.md); "
            + $"{examined} {rule.Layer} types examined in {modules.Count} modules. {BlindSpot} "
            + string.Join("; ", violations));
    }
}
