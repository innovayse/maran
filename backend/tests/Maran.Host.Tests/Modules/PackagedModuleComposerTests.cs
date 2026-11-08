using Maran.Host.Modules;
using Maran.Host.Tests.Modules.Fixtures;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Behavioral contract of <see cref="PackagedModuleComposer"/>: a packaged module joins the modules that
/// ship with the panel only if it passes every check, a refused one is reported and the rest of the panel
/// is unchanged, and each refusal sits beside the case that is accepted.
/// </summary>
/// <remarks>
/// <para>
/// The candidates are the real packageable modules the panel ships, offered as if they had arrived in a
/// package. They are honest modules in assemblies of their own, so the cases below differ from the
/// accepting one in exactly the thing under test: the set they are offered to. The agent-capability rule
/// is asserted in <c>Maran.ArchitectureTests</c>, because that rule inspects a whole assembly and this
/// test assembly holds agent-client fixtures of its own.
/// </para>
/// <para>
/// UNOBSERVED HERE: the reserved-assembly rule for a name such as <c>Maran.Sdk</c> is asserted on the
/// name predicate only; no module type can be defined in the Sdk's own assembly to offer as a candidate.
/// </para>
/// </remarks>
public sealed class PackagedModuleComposerTests
{
    /// <summary>A panel recent enough for every module the panel ships.</summary>
    private static readonly FixedPanelVersion NewPanel = new("99.0.0");

    /// <summary>A packageable module whose checks pass joins the shipped ones, after them.</summary>
    [Fact]
    public void A_packageable_module_that_passes_every_check_is_composed_after_the_shipped_ones()
    {
        var shipped = ShippedWithout("firewall");
        Assert.NotEmpty(shipped);

        var composition = PackagedModuleComposer.Compose(shipped, [ShippedModule("firewall")], NewPanel);

        Assert.Empty(composition.Refused);
        Assert.Equal("firewall", composition.Composed[^1].Manifest.Id);
        Assert.Equal(shipped.Count + 1, composition.Composed.Count);
        Assert.Equal(shipped, composition.Composed.Take(shipped.Count).ToList());
    }

    /// <summary>A package that claims the id of a composed module is refused and the composed set is unchanged.</summary>
    [Fact]
    public void A_package_claiming_the_id_of_a_composed_module_is_refused_and_the_set_is_unchanged()
    {
        var shipped = ModuleRegistry.All;

        var composition = PackagedModuleComposer.Compose(shipped, [ShippedModule("firewall")], NewPanel);

        var refusal = Assert.Single(composition.Refused);
        Assert.Equal("ModuleIdTaken", refusal.Reason.Code);
        Assert.Equal(ErrorType.Conflict, refusal.Reason.Type);
        Assert.Equal(shipped, composition.Composed);
    }

    /// <summary>A package that declares itself part of the core is refused.</summary>
    [Fact]
    public void A_package_that_declares_itself_core_is_refused()
    {
        var composition = PackagedModuleComposer.Compose(ShippedWithout("identity"), [ShippedModule("identity")], NewPanel);

        var refusal = Assert.Single(composition.Refused);
        Assert.Equal("ModuleNotPackageable", refusal.Reason.Code);
        Assert.DoesNotContain(composition.Composed, module => { return module.Manifest.Id == "identity"; });
    }

    /// <summary>A package whose dependency is not composed is refused, and accepted once the dependency is.</summary>
    [Fact]
    public void A_package_whose_dependency_is_not_composed_is_refused()
    {
        var withoutDependency = ModuleRegistry.All
            .Where(module => { return module.Manifest.Id is not ("ssl" or "sites"); })
            .ToList();

        var refused = PackagedModuleComposer.Compose(withoutDependency, [ShippedModule("ssl")], NewPanel);
        var accepted = PackagedModuleComposer.Compose(ShippedWithout("ssl"), [ShippedModule("ssl")], NewPanel);

        Assert.Equal("ModuleManifestInvalid", Assert.Single(refused.Refused).Reason.Code);
        Assert.Empty(accepted.Refused);
    }

    /// <summary>A package that needs a newer panel than the one running is refused.</summary>
    [Fact]
    public void A_package_that_needs_a_newer_panel_is_refused()
    {
        var composition = PackagedModuleComposer.Compose(
            ShippedWithout("firewall"), [ShippedModule("firewall")], new FixedPanelVersion("0.0.1"));

        Assert.Equal("ModuleManifestInvalid", Assert.Single(composition.Refused).Reason.Code);
    }

    /// <summary>A package loaded from the assembly of a composed module is refused.</summary>
    [Fact]
    public void A_package_loaded_from_the_assembly_of_a_composed_module_is_refused()
    {
        var resident = new StubPackagedModule(TestManifests.Of("resident"));
        var newcomer = new StubPackagedModule(TestManifests.Of("newcomer"));

        var composition = PackagedModuleComposer.Compose([resident], [newcomer], NewPanel);

        Assert.Equal("ModuleAssemblyReserved", Assert.Single(composition.Refused).Reason.Code);
        Assert.Same(resident, Assert.Single(composition.Composed));
    }

    /// <summary>One refused package does not stop the next from being composed.</summary>
    [Fact]
    public void A_refused_package_does_not_stop_the_next_one_being_composed()
    {
        var composition = PackagedModuleComposer.Compose(
            ShippedWithout("firewall"), [ShippedModule("identity"), ShippedModule("firewall")], NewPanel);

        var refused = Assert.Single(composition.Refused);
        Assert.NotNull(refused.Manifest);
        Assert.Equal("identity", refused.Manifest.Id);
        Assert.Contains(composition.Composed, module => { return module.Manifest.Id == "firewall"; });
    }

    /// <summary>A package whose manifest getter throws is refused as unreadable and the next package still composes.</summary>
    [Fact]
    public void A_package_whose_manifest_getter_throws_is_refused_and_the_next_one_still_composes()
    {
        var shipped = ShippedWithout("firewall");

        var composition = PackagedModuleComposer.Compose(
            shipped, [new ThrowingManifestModule(), ShippedModule("firewall")], NewPanel);

        var refusal = Assert.Single(composition.Refused);
        Assert.Equal("ModuleManifestUnreadable", refusal.Reason.Code);
        Assert.Equal(ErrorType.Validation, refusal.Reason.Type);
        Assert.Null(refusal.Manifest);
        Assert.Equal("firewall", composition.Composed[^1].Manifest.Id);
        Assert.Equal(shipped.Count + 1, composition.Composed.Count);
    }

    /// <summary>A package whose manifest getter answers null is refused as unreadable.</summary>
    [Fact]
    public void A_package_whose_manifest_getter_answers_null_is_refused()
    {
        var composition = PackagedModuleComposer.Compose(ModuleRegistry.All, [new NullManifestModule()], NewPanel);

        Assert.Equal("ModuleManifestUnreadable", Assert.Single(composition.Refused).Reason.Code);
        Assert.Equal(ModuleRegistry.All.Count, composition.Composed.Count);
    }

    /// <summary>A package whose manifest leaves a member null is refused as unreadable.</summary>
    [Fact]
    public void A_package_whose_manifest_has_a_null_member_is_refused()
    {
        var hollow = TestManifests.Of("hollow") with { Permissions = null! };

        var composition = PackagedModuleComposer.Compose(
            ModuleRegistry.All, [new StubPackagedModule(hollow)], NewPanel);

        var refusal = Assert.Single(composition.Refused);
        Assert.Equal("ModuleManifestUnreadable", refusal.Reason.Code);
        Assert.Null(refusal.Manifest);
    }

    /// <summary>The panel's reserved assemblies are named, and an ordinary module assembly is not.</summary>
    [Theory]
    [InlineData("Maran.Sdk", true)]
    [InlineData("Maran.SharedKernel", true)]
    [InlineData("Maran.Agent.Client", true)]
    [InlineData("Maran.Host", true)]
    [InlineData("Maran.Modules.Cron", false)]
    [InlineData("maran.sdk", false)]
    [InlineData(null, false)]
    public void The_assemblies_the_panel_reserves_are_named(string? assemblyName, bool reserved)
    {
        Assert.Equal(reserved, PackagedModuleComposer.IsReservedAssemblyName(assemblyName));
    }

    /// <summary>A panel version that is not a semantic version refuses to compose anything.</summary>
    [Fact]
    public void A_panel_version_that_is_not_semantic_refuses_to_compose_anything()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            return PackagedModuleComposer.Compose(ModuleRegistry.All, [], new FixedPanelVersion("next"));
        });
    }

    /// <summary>The modules the panel ships, less one.</summary>
    /// <param name="id">The id to leave out.</param>
    /// <returns>The remaining modules, in registration order.</returns>
    private static List<IPanelModule> ShippedWithout(string id)
    {
        return ModuleRegistry.All.Where(module => { return module.Manifest.Id != id; }).ToList();
    }

    /// <summary>One of the modules the panel ships, offered as if it had arrived in a package.</summary>
    /// <param name="id">The module id.</param>
    /// <returns>The module.</returns>
    private static IPanelModule ShippedModule(string id)
    {
        return ModuleRegistry.All.Single(module => { return module.Manifest.Id == id; });
    }

    /// <summary>A module whose manifest getter throws, as a broken package's would.</summary>
    private sealed class ThrowingManifestModule : IPanelModule
    {
        /// <inheritdoc />
        public string Name
        {
            get { return "throwing"; }
        }

        /// <inheritdoc />
        public Manifest Manifest
        {
            get { throw new InvalidOperationException("the manifest is not available"); }
        }

        /// <inheritdoc />
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // Never composed: the composer refuses it first.
        }
    }

    /// <summary>A module whose manifest getter answers null, as a package built without nullable checks could.</summary>
    private sealed class NullManifestModule : IPanelModule
    {
        /// <inheritdoc />
        public string Name
        {
            get { return "null-manifest"; }
        }

        /// <inheritdoc />
        public Manifest Manifest
        {
            get { return null!; }
        }

        /// <inheritdoc />
        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // Never composed: the composer refuses it first.
        }
    }
}
