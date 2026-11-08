using Maran.ArchitectureTests.Fixtures;
using Maran.Host.Modules;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.ArchitectureTests;

/// <summary>
/// A packageable module references the Sdk and the SharedKernel, the agent client only when its
/// manifest declares agent capabilities, and nothing else but the packages in
/// <c>ModuleReferenceAllowList.txt</c> (rules/architecture.md "Packageable module contract").
/// </summary>
/// <remarks>
/// <para>
/// A packaged module is code the repository never reviews, so the contract is held here for the
/// modules that could become one: <see cref="ModuleKind.Packageable"/>. Core modules are bound by the
/// module-isolation tests (no other module, no Host) and are deliberately not bound by the package
/// allow-list, because they never leave the repository. Every rule is shown on a planted project
/// file (<see cref="PlantedPackageReferencingHost"/>) and on a clean one.
/// </para>
/// <para>
/// The census: the packageable set must be non-empty, because the allow-list rules over an empty set
/// are satisfied by every list; and every entry of the allow-list must be used by some packageable
/// module, so the list cannot keep an allowance that nothing needs.
/// </para>
/// </remarks>
public sealed class ModuleSdkOnlyReferenceTests
{
    /// <summary>The module folder prefix of the assembly names.</summary>
    private const string AssemblyPrefix = "Maran.Modules.";

    /// <summary>Every packageable module's project file obeys the reference contract.</summary>
    [Fact]
    public void Every_packageable_modules_project_file_obeys_the_reference_contract()
    {
        var allowList = ModuleReferenceAllowList.Load();
        var violations = new List<string>();

        foreach (var module in PackageableModules())
        {
            violations.AddRange(ModuleReferenceLaw.ProjectViolations(
                module.Manifest.Id,
                File.ReadAllText(ProjectFileOf(module)),
                module.Manifest.AgentCapabilities.Count > 0,
                allowList));
        }

        Assert.True(
            violations.Count == 0,
            "A packageable module references more than the contract allows. Add a package to "
            + "ModuleReferenceAllowList.txt only as a reviewed change with a reason:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>Every packageable module is compiled against the Sdk, the SharedKernel and a declared agent client only.</summary>
    [Fact]
    public void Every_packageable_module_is_compiled_against_product_assemblies_the_contract_allows()
    {
        var violations = PackageableModules().SelectMany(module =>
        {
            var names = module.GetType().Assembly.GetReferencedAssemblies()
                .Select(reference => { return reference.Name!; });
            return ModuleReferenceLaw.CompiledViolations(
                module.Manifest.Id,
                names,
                module.Manifest.AgentCapabilities.Count > 0);
        }).ToList();

        Assert.True(
            violations.Count == 0,
            "A packageable module is compiled against a product assembly the contract does not allow:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>The census examined packageable modules, and the allow-list allows nothing no module uses.</summary>
    [Fact]
    public void The_census_examined_packageable_modules_and_the_allow_list_has_no_unused_entry()
    {
        var packageable = PackageableModules();
        var texts = packageable.Select(module => { return File.ReadAllText(ProjectFileOf(module)); }).ToList();

        Assert.NotEmpty(packageable);
        Assert.Empty(ModuleReferenceLaw.UnusedEntries(texts, ModuleReferenceAllowList.Load()));
    }

    /// <summary>A project file referencing the Host, another module, an unlisted package and a bare assembly is flagged by rule.</summary>
    [Fact]
    public void A_planted_project_file_breaking_every_rule_is_flagged_by_rule()
    {
        var violations = ModuleReferenceLaw.ProjectViolations(
            "planted",
            PlantedPackageReferencingHost.BadProject,
            declaresAgentCapabilities: false,
            ModuleReferenceAllowList.Load());

        Assert.Contains(violations, line => { return line.Contains("project Maran.Host", StringComparison.Ordinal) && line.StartsWith(ModuleReferenceLaw.ProjectReferenceNotAllowed, StringComparison.Ordinal); });
        Assert.Contains(violations, line => { return line.Contains("Maran.Modules.Accounts", StringComparison.Ordinal) && line.StartsWith(ModuleReferenceLaw.ProjectReferenceNotAllowed, StringComparison.Ordinal); });
        Assert.Contains(violations, line => { return line.StartsWith(ModuleReferenceLaw.PackageNotAllowListed, StringComparison.Ordinal); });
        Assert.Contains(violations, line => { return line.StartsWith(ModuleReferenceLaw.FrameworkNotAllowListed, StringComparison.Ordinal); });
        Assert.Contains(violations, line => { return line.StartsWith(ModuleReferenceLaw.AssemblyReferenceNotAllowed, StringComparison.Ordinal); });
    }

    /// <summary>The agent client is accepted with a declared capability and flagged without one.</summary>
    [Fact]
    public void The_agent_client_is_accepted_when_declared_and_flagged_when_not()
    {
        var allowList = ModuleReferenceAllowList.Load();

        var declared = ModuleReferenceLaw.ProjectViolations(
            "planted", PlantedPackageReferencingHost.CleanProject, declaresAgentCapabilities: true, allowList);
        var undeclared = ModuleReferenceLaw.ProjectViolations(
            "planted", PlantedPackageReferencingHost.CleanProject, declaresAgentCapabilities: false, allowList);

        Assert.Empty(declared);
        Assert.Contains(undeclared, line => { return line.StartsWith(ModuleReferenceLaw.AgentClientNotDeclared, StringComparison.Ordinal); });
    }

    /// <summary>A compiled reference to the Host or to another module is flagged, and the Sdk and the kernel are accepted.</summary>
    [Fact]
    public void A_planted_compiled_reference_to_the_host_is_flagged_and_the_sdk_is_accepted()
    {
        var accepted = ModuleReferenceLaw.CompiledViolations(
            "planted",
            ["System.Runtime", "Maran.Sdk", "Maran.SharedKernel", "Maran.Agent.Client"],
            declaresAgentCapabilities: true);
        var flagged = ModuleReferenceLaw.CompiledViolations(
            "planted",
            ["Maran.Sdk", "Maran.Host", "Maran.Modules.Accounts", "Maran.Agent.Client"],
            declaresAgentCapabilities: false);

        Assert.Empty(accepted);
        Assert.Equal(3, flagged.Count);
        Assert.All(flagged, line => { Assert.StartsWith(ModuleReferenceLaw.CompiledReferenceNotAllowed, line, StringComparison.Ordinal); });
    }

    /// <summary>An allow-list entry nobody uses is flagged, and the parser refuses a line without a reason.</summary>
    [Fact]
    public void A_planted_unused_allow_list_entry_is_flagged_and_a_line_without_a_reason_is_refused()
    {
        var withExtra = ModuleReferenceAllowList.Parse(
            "package|WolverineFx|messages\npackage|Never.Used|planted\nframework|Microsoft.AspNetCore.App|web");

        var unused = ModuleReferenceLaw.UnusedEntries([PlantedPackageReferencingHost.CleanProject], withExtra);

        Assert.Single(unused);
        Assert.StartsWith(ModuleReferenceLaw.AllowListEntryUnused, unused[0], StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => { return ModuleReferenceAllowList.Parse("package|WolverineFx|"); });
    }

    /// <summary>The composed modules that are packageable.</summary>
    /// <returns>The modules, in registry order. Licensing is not among them: it is core.</returns>
    private static List<IPanelModule> PackageableModules()
    {
        return ModuleRegistry.All
            .Where(module => { return module.Manifest.Kind == ModuleKind.Packageable; })
            .ToList();
    }

    /// <summary>Locates a module's project file.</summary>
    /// <param name="module">The module.</param>
    /// <returns>Absolute path of its <c>.csproj</c>.</returns>
    private static string ProjectFileOf(IPanelModule module)
    {
        var assemblyName = module.GetType().Assembly.GetName().Name!;
        return Path.Combine(
            ResourceTriples.BackendRoot(),
            "src",
            "Maran.Modules",
            assemblyName[AssemblyPrefix.Length..],
            assemblyName + ".csproj");
    }
}
