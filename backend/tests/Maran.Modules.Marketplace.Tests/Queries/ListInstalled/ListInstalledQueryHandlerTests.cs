using Maran.Modules.Marketplace.Common;
using Maran.Modules.Marketplace.Queries.ListInstalled;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.Queries.ListInstalled;

/// <summary>The installed list is what the loader runs beside the ledger's history, read without the marketplace.</summary>
public sealed class ListInstalledQueryHandlerTests
{
    /// <summary>Lists the installed modules of a world.</summary>
    /// <param name="world">The world.</param>
    /// <returns>The installed modules.</returns>
    private static async Task<IReadOnlyList<InstalledModuleDto>> ListAsync(OperationWorld world)
    {
        var listed = await new ListInstalledQueryHandler(world.Installed, world.Names)
            .HandleAsync(new ListInstalledQuery(), CancellationToken.None);
        return listed.Value;
    }

    /// <summary>With the marketplace off and unreachable the installed modules are still listed, and no marketplace call is made.</summary>
    [Fact]
    public async Task The_installed_list_needs_no_marketplace()
    {
        using var world = new OperationWorld();
        await world.Settings.DisableAsync(world.UserId, CancellationToken.None);
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Active);

        var modules = await ListAsync(world);

        var mail = Assert.Single(modules);
        Assert.Equal("mail", mail.Id);
        Assert.Equal(ModuleLifecycleState.Active, mail.Lifecycle);
        Assert.True(mail.CanRemove);
        Assert.False(mail.RestartRequired);
        Assert.Equal(0, world.Client.ReadCalls);
    }

    /// <summary>A staged module the loader now reports as running is closed in the ledger, so it no longer asks for a restart.</summary>
    [Fact]
    public async Task A_staged_module_that_now_runs_stops_asking_for_a_restart()
    {
        using var world = new OperationWorld();
        await world.Ledger.RecordStagedAsync("mail", "1.0.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Installed);

        var before = await ListAsync(world);
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Active);
        var after = await ListAsync(world);

        Assert.True(before[0].RestartRequired);
        Assert.False(after[0].RestartRequired);
        Assert.NotNull(after[0].InstalledAt);
    }

    /// <summary>A failed module carries the sentence for its failure code; a running one carries none.</summary>
    [Fact]
    public async Task A_failed_module_carries_its_failure_in_words()
    {
        using var world = new OperationWorld();
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Active);
        world.Loader.Seed(
            "dns", "1.0.0", ModuleLifecycleState.Failed, Error.Of("ModuleManifestInvalid", ErrorType.Validation));

        var modules = await ListAsync(world);

        var dns = modules.Single(module => { return module.Id == "dns"; });
        var mail = modules.Single(module => { return module.Id == "mail"; });
        Assert.Equal(EchoErrorTextProvider.Prefix + "ModuleManifestInvalid", dns.FailureMessage);
        Assert.Null(mail.FailureMessage);
        Assert.Equal("marketplace test", mail.Origin);
    }

    /// <summary>A module the last update check found withdrawn is marked revoked in the list; before any check nothing is.</summary>
    [Fact]
    public async Task A_withdrawn_module_is_marked_after_an_update_check()
    {
        using var world = new OperationWorld();
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Active);

        var before = await ListAsync(world);
        world.Revocations.Replace(["mail"]);
        var after = await ListAsync(world);

        Assert.False(before[0].Revoked);
        Assert.True(after[0].Revoked);
    }
}
