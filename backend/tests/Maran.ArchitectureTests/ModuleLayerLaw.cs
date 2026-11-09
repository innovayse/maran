using System.Reflection;
using NetArchTest.Rules;

namespace Maran.ArchitectureTests;

/// <summary>
/// The layering inside a module, as data, and the one walk that judges it (rules/architecture.md,
/// "Inside a module"). The production tests and the planted controls call the same two methods, so a
/// control cannot pass through a different path from the one the real modules take.
/// </summary>
/// <remarks>
/// <b>What it observes:</b> every type reference NetArchTest reads from the compiled IL — base types,
/// fields, properties, signatures and method bodies. <b>What it does not:</b> dependencies through
/// reflection, string-built type names, or the DI container.
/// </remarks>
public static class ModuleLayerLaw
{
    /// <summary>The Domain layer reaches no application layer and no EF Core: the model, not the plumbing.</summary>
    public static readonly ModuleLayerRule DomainRule = new(
        "Domain",
        ["Persistence", "Commands", "Queries", "Controllers"],
        ["Microsoft.EntityFrameworkCore"]);

    /// <summary>A controller binds, dispatches and translates; it never opens the module's data.</summary>
    public static readonly ModuleLayerRule ControllersRule = new(
        "Controllers",
        ["Persistence"],
        []);

    /// <summary>Every rule of the law.</summary>
    public static readonly IReadOnlyList<ModuleLayerRule> Rules = [DomainRule, ControllersRule];

    /// <summary>Names every type of the rule's layer that depends on something the layer must not.</summary>
    /// <param name="assembly">The assembly holding the module (or the planted fixtures).</param>
    /// <param name="moduleRoot">The module's root namespace, such as <c>Maran.Modules.Sites</c>.</param>
    /// <param name="rule">The rule to apply.</param>
    /// <returns>
    /// One <c>"&lt;type&gt; (&lt;layer&gt;) depends on &lt;forbidden&gt;"</c> line per offence, empty when clean.
    /// </returns>
    public static List<string> Violations(Assembly assembly, string moduleRoot, ModuleLayerRule rule)
    {
        var forbidden = rule.ForbiddenModuleLayers
            .Select(layer =>
            {
                return $"{moduleRoot}.{layer}";
            })
            .Concat(rule.ForbiddenNamespaces);

        var violations = new List<string>();
        foreach (var target in forbidden)
        {
            var result = Types.InAssembly(assembly)
                .That().ResideInNamespaceStartingWith(LayerNamespace(moduleRoot, rule))
                .ShouldNot().HaveDependencyOn(target)
                .GetResult();

            violations.AddRange((result.FailingTypeNames ?? []).Select(name =>
            {
                return $"{name} ({rule.Layer}) depends on {target}";
            }));
        }

        return violations;
    }

    /// <summary>Counts the types of the rule's layer, so a test can prove the walk looked at something.</summary>
    /// <param name="assembly">The assembly holding the module (or the planted fixtures).</param>
    /// <param name="moduleRoot">The module's root namespace.</param>
    /// <param name="rule">The rule whose layer is counted.</param>
    /// <returns>The number of types declared in that layer's namespace or below it.</returns>
    public static int LayerTypeCount(Assembly assembly, string moduleRoot, ModuleLayerRule rule)
    {
        return Types.InAssembly(assembly)
            .That().ResideInNamespaceStartingWith(LayerNamespace(moduleRoot, rule))
            .GetTypes()
            .Count();
    }

    /// <summary>The namespace a rule's layer occupies inside a module.</summary>
    /// <param name="moduleRoot">The module's root namespace.</param>
    /// <param name="rule">The rule.</param>
    /// <returns>For example <c>Maran.Modules.Sites.Domain</c>.</returns>
    private static string LayerNamespace(string moduleRoot, ModuleLayerRule rule)
    {
        return $"{moduleRoot}.{rule.Layer}";
    }
}
