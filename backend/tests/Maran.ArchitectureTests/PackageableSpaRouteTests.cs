using Maran.Host.Modules;
using Maran.Sdk.Contracts;

namespace Maran.ArchitectureTests;

/// <summary>
/// The SPA's router does not declare more routes for a packageable module than it did when the module
/// contract was written: a packageable module's menu and routes come from its manifest.
/// </summary>
/// <remarks>
/// <para>
/// Every module's screens are still compiled into the core SPA, so the router names packageable
/// modules today. This gate is a ratchet (<see cref="PackageableSpaRouteCensus"/>): it forbids ADDING
/// one and makes lowering the baseline a visible edit when one is removed.
/// </para>
/// <para>
/// The census guard is on the axis that can go blind: the router is read from the repository and must
/// name at least one module, because a moved file or a changed spelling would otherwise count zero
/// everywhere and pass.
/// </para>
/// </remarks>
public sealed class PackageableSpaRouteTests
{
    /// <summary>No packageable module has more routes in the SPA's router than the baseline records.</summary>
    [Fact]
    public void No_packageable_module_gains_a_route_in_the_spa_router()
    {
        var counts = PackageableSpaRouteCensus.CountsOf(File.ReadAllText(RouterPath()));
        var baseline = PackageableSpaRouteCensus.ParseBaseline(File.ReadAllText(BaselinePath()));
        var packageable = ModuleRegistry.All
            .Where(module => { return module.Manifest.Kind == ModuleKind.Packageable; })
            .Select(module => { return module.Manifest.Id; })
            .ToList();

        Assert.NotEmpty(packageable);
        var violations = PackageableSpaRouteCensus.Violations(counts, baseline, packageable);
        Assert.True(
            violations.Count == 0,
            "The SPA's router and the packageable-module baseline disagree (docs/modules/module-contract.md):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    /// <summary>The census reads the real router and finds modules in it.</summary>
    [Fact]
    public void The_census_finds_modules_in_the_real_router()
    {
        var counts = PackageableSpaRouteCensus.CountsOf(File.ReadAllText(RouterPath()));

        Assert.NotEmpty(counts);
        Assert.True(counts.GetValueOrDefault("accounts") > 0, "The router names no accounts route; the census may be reading the wrong spelling.");
    }

    /// <summary>A planted extra route is flagged, a route at the baseline is accepted, and a fallen count asks for the baseline to follow.</summary>
    [Fact]
    public void A_planted_extra_route_is_flagged_and_a_route_at_the_baseline_is_accepted()
    {
        var router = "{ meta: { module: 'widgets', title: 'a' } }\n{ meta: { module: 'widgets', title: 'b' } }\n";
        var counts = PackageableSpaRouteCensus.CountsOf(router);
        var ids = new[] { "widgets" };

        var rose = PackageableSpaRouteCensus.Violations(counts, PackageableSpaRouteCensus.ParseBaseline("widgets|1"), ids);
        var held = PackageableSpaRouteCensus.Violations(counts, PackageableSpaRouteCensus.ParseBaseline("widgets|2"), ids);
        var fell = PackageableSpaRouteCensus.Violations(counts, PackageableSpaRouteCensus.ParseBaseline("widgets|3"), ids);
        var unrecorded = PackageableSpaRouteCensus.Violations(counts, PackageableSpaRouteCensus.ParseBaseline(string.Empty), ids);

        Assert.Single(rose);
        Assert.Empty(held);
        Assert.Single(fell);
        Assert.Single(unrecorded);
    }

    /// <summary>Locates the SPA's router file.</summary>
    /// <returns>Absolute path of <c>frontend/src/router/index.ts</c>.</returns>
    private static string RouterPath()
    {
        var path = Path.Combine(SpaLocaleKeys.RepositoryRoot(), "frontend", "src", "router", "index.ts");
        Assert.True(File.Exists(path), $"The SPA router was not found at {path}.");
        return path;
    }

    /// <summary>Locates the baseline file in the architecture test project.</summary>
    /// <returns>Absolute path of <c>PackageableSpaRouteBaseline.txt</c>.</returns>
    private static string BaselinePath()
    {
        var path = Path.Combine(ResourceTriples.BackendRoot(), "tests", "Maran.ArchitectureTests", "PackageableSpaRouteBaseline.txt");
        Assert.True(File.Exists(path), $"The route baseline was not found at {path}.");
        return path;
    }
}
