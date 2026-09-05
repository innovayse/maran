using System.Reflection;
using NetArchTest.Rules;

namespace Maran.ArchitectureTests;

/// <summary>CI-enforced module isolation (rules/architecture.md).</summary>
public sealed class ModuleIsolationTests
{
    /// <summary>The namespace prefix every module project shares.</summary>
    private const string ModulesPrefix = "Maran.Modules.";

    /// <summary>Modules reference only sdk and shared kernel.</summary>
    [Fact]
    public void Modules_reference_only_sdk_and_shared_kernel()
    {
        var result = Types.InCurrentDomain()
            .That().ResideInNamespaceStartingWith("Maran.Modules")
            .ShouldNot().HaveDependencyOnAny("Maran.Host")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>No module depends on another module.</summary>
    /// <remarks>
    /// rules/architecture.md says "Referencing another module is forbidden and fails the NetArchTest
    /// suite", and until this test existed that sentence was false: the only assertion here banned
    /// <c>Maran.Host</c>, so a real <c>Sites → Accounts</c> ProjectReference passed the whole suite.
    /// The Sites module's <c>IAccountDirectory</c> exists specifically to avoid such a reference, and
    /// a design that avoids something nothing forbids is a convention, not a boundary.
    ///
    /// The pairs are derived from the assemblies actually loaded rather than written out by hand, so
    /// a module added later is covered without anyone remembering to extend a list —
    /// <see cref="ModuleCoverageTests"/> is what guarantees those assemblies are present.
    /// </remarks>
    [Fact]
    public void No_module_depends_on_another_module()
    {
        var moduleNamespaces = ModuleNamespaces();
        Assert.NotEmpty(moduleNamespaces);

        var violations = new List<string>();
        foreach (var owner in moduleNamespaces)
        {
            var forbidden = moduleNamespaces
                .Where(other =>
                {
                    return !string.Equals(other, owner, StringComparison.Ordinal);
                })
                .ToArray();
            if (forbidden.Length == 0)
            {
                continue;
            }

            var result = Types.InCurrentDomain()
                .That().ResideInNamespaceStartingWith(owner)
                .ShouldNot().HaveDependencyOnAny(forbidden)
                .GetResult();

            if (!result.IsSuccessful)
            {
                violations.AddRange((result.FailingTypeNames ?? []).Select(name =>
                {
                    return $"{name} (in {owner}) depends on another module";
                }));
            }
        }

        Assert.True(
            violations.Count == 0,
            "A module may reference only Maran.Sdk and Maran.SharedKernel (rules/architecture.md). "
            + "Cross-module needs go through Wolverine messages or Sdk abstractions: "
            + string.Join("; ", violations));
    }

    /// <summary>Shared kernel depends on nothing of ours.</summary>
    [Fact]
    public void Shared_kernel_depends_on_nothing_of_ours()
    {
        var result = Types.InCurrentDomain()
            .That().ResideInNamespaceStartingWith("Maran.SharedKernel")
            .ShouldNot().HaveDependencyOnAny("Maran.Host", "Maran.Sdk", "Maran.Modules", "Maran.Agent.Client")
            .GetResult();

        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>The root namespace of every module assembly loaded into this test run.</summary>
    /// <returns>Names such as <c>Maran.Modules.Sites</c>.</returns>
    private static List<string> ModuleNamespaces()
    {
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "Maran.Modules.*.dll"))
        {
            try
            {
                Assembly.LoadFrom(path);
            }
            catch (BadImageFormatException)
            {
                // Native or mixed-mode files matching the pattern are not managed assemblies.
            }
        }

        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly =>
            {
                return assembly.GetName().Name;
            })
            .Where(name =>
            {
                return name is not null
                    && name.StartsWith(ModulesPrefix, StringComparison.Ordinal)
                    && !name.EndsWith(".Tests", StringComparison.Ordinal);
            })
            .Select(name =>
            {
                return name!;
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
