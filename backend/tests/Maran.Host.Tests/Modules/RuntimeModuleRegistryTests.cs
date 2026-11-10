using Maran.Host.Modules;
using Maran.Host.Tests.Modules.Fixtures;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Modules;

/// <summary>
/// Behavioral contract of <see cref="RuntimeModuleRegistry"/>: it lists what the start composed, takes
/// the state of a packaged module from the loader, never lets the loader touch a shipped module, and
/// lists what was refused.
/// </summary>
public sealed class RuntimeModuleRegistryTests
{
    /// <summary>A panel recent enough for every module the panel ships.</summary>
    private static readonly FixedPanelVersion NewPanel = new("99.0.0");

    /// <summary>With no word from the loader, every composed module is active.</summary>
    [Fact]
    public async Task With_no_word_from_the_loader_every_composed_module_is_active()
    {
        var registry = RegistryOf(Compose("firewall"), new StubModuleLoader());

        var listed = await registry.ListAsync(CancellationToken.None);

        Assert.Equal(ModuleRegistry.All.Count, listed.Count);
        Assert.All(listed, module => { Assert.Equal(ModuleLifecycleState.Active, module.State); });
    }

    /// <summary>A degraded packaged module is reported degraded, and its siblings stay active.</summary>
    [Fact]
    public async Task A_packaged_module_the_loader_reports_degraded_is_degraded_and_nothing_else_is()
    {
        var loader = new StubModuleLoader(Held("firewall", ModuleLifecycleState.Degraded));

        var listed = await RegistryOf(Compose("firewall"), loader).ListAsync(CancellationToken.None);

        Assert.Equal(ModuleLifecycleState.Degraded, StateOf(listed, "firewall"));
        Assert.Equal(ModuleRegistry.All.Count - 1, listed.Count(module => { return module.State == ModuleLifecycleState.Active; }));
    }

    /// <summary>A shipped module is never degraded, whatever the loader says about its id.</summary>
    [Fact]
    public async Task A_shipped_module_is_never_degraded_whatever_the_loader_reports()
    {
        var loader = new StubModuleLoader(
            Held("identity", ModuleLifecycleState.Degraded),
            Held("cron", ModuleLifecycleState.Failed));

        var listed = await RegistryOf(Compose("firewall"), loader).ListAsync(CancellationToken.None);

        Assert.Equal(ModuleLifecycleState.Active, StateOf(listed, "identity"));
        Assert.Equal(ModuleLifecycleState.Active, StateOf(listed, "cron"));
    }

    /// <summary>A newer version waiting for the next start does not change what the running module reports.</summary>
    [Fact]
    public async Task A_version_waiting_for_the_next_start_leaves_the_running_module_active()
    {
        var loader = new StubModuleLoader(Held("firewall", ModuleLifecycleState.Installed));

        var listed = await RegistryOf(Compose("firewall"), loader).ListAsync(CancellationToken.None);

        Assert.Equal(ModuleLifecycleState.Active, StateOf(listed, "firewall"));
    }

    /// <summary>A refused package is listed as failed, after the composed modules.</summary>
    [Fact]
    public async Task A_refused_package_is_listed_as_failed_after_the_composed_modules()
    {
        var composition = PackagedModuleComposer.Compose(
            ModuleRegistry.All, [ModuleRegistry.All.Single(module => { return module.Manifest.Id == "firewall"; })], NewPanel);

        var listed = await RegistryOf(composition, new StubModuleLoader()).ListAsync(CancellationToken.None);

        Assert.Equal(ModuleRegistry.All.Count + 1, listed.Count);
        Assert.Equal(("firewall", ModuleLifecycleState.Failed), (listed[^1].Id, listed[^1].State));
        Assert.Equal(ModuleLifecycleState.Active, listed.First(module => { return module.Id == "firewall"; }).State);
    }

    /// <summary>The cause the loader reports for a degraded module travels with its state; an active module has none.</summary>
    [Fact]
    public async Task The_cause_the_loader_reports_for_a_degraded_module_travels_with_its_state()
    {
        var cause = Error.Of("SomeCause", ErrorType.Forbidden);
        var loader = new StubModuleLoader(
            new LoadedModuleInfo("firewall", "1.0.0", ModuleLifecycleState.Degraded, "test", cause));

        var listed = await RegistryOf(Compose("firewall"), loader).ListAsync(CancellationToken.None);

        Assert.Equal(cause, listed.Single(module => { return module.Id == "firewall"; }).Failure);
        Assert.All(
            listed.Where(module => { return module.Id != "firewall"; }),
            module => { Assert.Null(module.Failure); });
    }

    /// <summary>Only a composed packaged module is packaged; a shipped one is not.</summary>
    [Fact]
    public void Only_a_composed_packaged_module_is_packaged()
    {
        var composition = Compose("firewall");
        var registry = RegistryOf(composition, new StubModuleLoader());

        Assert.True(registry.IsPackaged(composition.Packaged.Single()));
        Assert.All(
            composition.Composed.Where(module => { return !composition.Packaged.Contains(module); }),
            module => { Assert.False(registry.IsPackaged(module)); });
    }

    /// <summary>The registry exposes the composed modules for the catalogue to read.</summary>
    [Fact]
    public void The_registry_exposes_the_composed_modules()
    {
        var composition = Compose("firewall");

        Assert.Same(composition.Composed, RegistryOf(composition, new StubModuleLoader()).Modules);
    }

    /// <summary>Composes the shipped modules with one of them offered again as a package.</summary>
    /// <param name="packagedId">The id of the shipped module to leave out of the shipped set and offer as a package.</param>
    /// <returns>The composition.</returns>
    private static PackagedModuleComposition Compose(string packagedId)
    {
        var shipped = ModuleRegistry.All.Where(module => { return module.Manifest.Id != packagedId; }).ToList();
        var packaged = ModuleRegistry.All.Single(module => { return module.Manifest.Id == packagedId; });

        return PackagedModuleComposer.Compose(shipped, [packaged], NewPanel);
    }

    /// <summary>Builds the registry under test.</summary>
    /// <param name="composition">What the start composed.</param>
    /// <param name="loader">The loader that reports packaged state.</param>
    /// <returns>The registry.</returns>
    private static RuntimeModuleRegistry RegistryOf(PackagedModuleComposition composition, IModuleLoader loader)
    {
        return new RuntimeModuleRegistry(composition, loader);
    }

    /// <summary>One module as the loader reports it held.</summary>
    /// <param name="id">The module id.</param>
    /// <param name="state">The state it reports.</param>
    /// <returns>The report.</returns>
    private static LoadedModuleInfo Held(string id, ModuleLifecycleState state)
    {
        return new LoadedModuleInfo(id, "1.0.0", state, "test", null);
    }

    /// <summary>The state listed for one module.</summary>
    /// <param name="listed">The registry's list.</param>
    /// <param name="id">The module id.</param>
    /// <returns>Its state.</returns>
    private static ModuleLifecycleState StateOf(IReadOnlyList<RegisteredModule> listed, string id)
    {
        return listed.Single(module => { return module.Id == id; }).State;
    }
}
