using Maran.Host.Modules;
using Maran.Host.Tests.Modules.Fixtures;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Behavioral contract of <see cref="ModuleManifestGuard"/>: the composition root refuses a set of
/// manifests that breaks the module contract, naming every finding, and accepts the one it ships.
/// </summary>
/// <remarks>
/// The rules themselves are tested where they live (<c>ManifestValidatorTests</c>); what is asserted
/// here is that the Host actually applies them, with a refusing case beside the accepting one and a
/// census guard so an emptied registry cannot pass.
/// </remarks>
public sealed class ModuleManifestGuardTests
{
    /// <summary>A panel version stated by a test.</summary>
    private sealed class FixedPanelVersion : IPanelVersion
    {
        /// <summary>Creates the stand-in.</summary>
        /// <param name="current">The version it reports.</param>
        public FixedPanelVersion(string current)
        {
            Current = current;
        }

        /// <inheritdoc />
        public string Current { get; }
    }

    /// <summary>A set whose dependencies are present and in range is accepted (accepting control).</summary>
    [Fact]
    public void A_set_that_keeps_the_module_contract_is_accepted()
    {
        var set = new[]
        {
            TestManifests.Of("sites", "1.4.0"),
            TestManifests.Of("ssl", dependencies: [new ModuleDependency("sites", ">=1.0.0 <2.0.0")]),
        };

        ModuleManifestGuard.Verify(set, new FixedPanelVersion("1.0.0"));
    }

    /// <summary>A dependency on a module that is not composed is refused, naming the module and the dependency.</summary>
    [Fact]
    public void A_dependency_on_a_module_that_is_not_composed_is_refused()
    {
        var set = new[] { TestManifests.Of("ssl", dependencies: [new ModuleDependency("sites", ">=1.0.0")]) };

        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            ModuleManifestGuard.Verify(set, new FixedPanelVersion("1.0.0"));
        });

        Assert.Contains("ssl: DependencyUnknown (sites)", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A dependency outside the range asked for is refused.</summary>
    [Fact]
    public void A_dependency_outside_its_range_is_refused()
    {
        var set = new[]
        {
            TestManifests.Of("sites", "1.9.0"),
            TestManifests.Of("ssl", dependencies: [new ModuleDependency("sites", ">=2.0.0")]),
        };

        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            ModuleManifestGuard.Verify(set, new FixedPanelVersion("1.0.0"));
        });

        Assert.Contains("ssl: DependencyVersionUnsatisfied", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A dependency loop is refused.</summary>
    [Fact]
    public void A_dependency_loop_is_refused()
    {
        var set = new[]
        {
            TestManifests.Of("a", dependencies: [new ModuleDependency("b", ">=1.0.0")]),
            TestManifests.Of("b", dependencies: [new ModuleDependency("a", ">=1.0.0")]),
        };

        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            ModuleManifestGuard.Verify(set, new FixedPanelVersion("1.0.0"));
        });

        Assert.Contains("DependencyCycle", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A module that needs a newer panel than the running one is refused, and accepted on the panel it needs.</summary>
    [Fact]
    public void A_module_that_needs_a_newer_panel_is_refused_and_accepted_on_the_panel_it_needs()
    {
        var set = new[] { TestManifests.Of("sites") with { MinPanelVersion = "2.0.0" } };

        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            ModuleManifestGuard.Verify(set, new FixedPanelVersion("1.0.0"));
        });
        Assert.Contains("sites: MinPanelVersionAbovePanel (2.0.0)", failure.Message, StringComparison.Ordinal);

        ModuleManifestGuard.Verify(set, new FixedPanelVersion("2.0.0"));
    }

    /// <summary>One run names every finding in an order that does not depend on the order of the set.</summary>
    [Fact]
    public void One_run_names_every_finding_in_a_stable_order()
    {
        var set = new[]
        {
            TestManifests.Of("x", dependencies: [new ModuleDependency("gone", ">=1.0.0")]),
            TestManifests.Of("y", dependencies: [new ModuleDependency("missing", ">=1.0.0")]),
        };

        var one = Assert.Throws<InvalidOperationException>(() =>
        {
            ModuleManifestGuard.Verify(set, new FixedPanelVersion("1.0.0"));
        });
        var other = Assert.Throws<InvalidOperationException>(() =>
        {
            ModuleManifestGuard.Verify(set.Reverse().ToList(), new FixedPanelVersion("1.0.0"));
        });

        Assert.Contains("x: DependencyUnknown (gone)", one.Message, StringComparison.Ordinal);
        Assert.Contains("y: DependencyUnknown (missing)", one.Message, StringComparison.Ordinal);
        Assert.Equal(one.Message, other.Message);
    }

    /// <summary>A panel version that is not semantic refuses every module instead of comparing against nothing.</summary>
    [Fact]
    public void A_panel_version_that_is_not_semantic_refuses_the_set()
    {
        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            ModuleManifestGuard.Verify([TestManifests.Of("sites")], new FixedPanelVersion("nightly"));
        });

        Assert.Contains("'nightly' is not a semantic version", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The manifests the panel composes satisfy the guard on the panel's own version, over a registry that is not empty.</summary>
    [Fact]
    public void The_manifests_the_panel_composes_satisfy_the_guard()
    {
        var manifests = ModuleRegistry.All.Select(module => { return module.Manifest; }).ToList();

        Assert.NotEmpty(manifests);
        Assert.Contains(manifests, manifest => { return manifest.Dependencies.Count > 0; });

        ModuleManifestGuard.Verify(manifests, new PanelVersion());
    }
}
