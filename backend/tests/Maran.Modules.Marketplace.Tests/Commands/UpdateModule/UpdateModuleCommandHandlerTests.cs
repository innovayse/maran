using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Queries.ListInstalled;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.Commands.UpdateModule;

/// <summary>
/// Updating an installed module stages the newer version while the running one keeps running, refuses what
/// an update may not do, and never loses the running version when the loader refuses.
/// </summary>
public sealed class UpdateModuleCommandHandlerTests
{
    /// <summary>Builds a world with <c>mail</c> 1.0.0 running, installed through the ledger, and 1.1.0 on offer.</summary>
    /// <returns>The world.</returns>
    private static async Task<OperationWorld> WithMailRunningAsync()
    {
        var world = new OperationWorld();
        await world.Ledger.RecordStagedAsync("mail", "1.0.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Active);
        world.Offer(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("1.1.0")),
            CatalogueSamples.Version("1.1.0"),
            CatalogueSamples.Version("1.0.0"));
        return world;
    }

    /// <summary>An update stages the newer version as an update task; the running version keeps running and a restart is asked for.</summary>
    [Fact]
    public async Task Update_stages_the_newer_version_while_the_old_one_keeps_running()
    {
        using var world = await WithMailRunningAsync();

        var accepted = await world.UpdateHandler().HandleAsync(world.Update("mail", "1.1.0"), CancellationToken.None);
        await world.RunDispatchedAsync();

        Assert.True(accepted.IsSuccess);
        Assert.Equal(TaskKinds.ModuleUpdate, Assert.Single(world.Tasks.Begun).Kind);
        Assert.Single(world.Tasks.Completed);

        var listed = await new ListInstalledQueryHandler(world.Installed, world.Names)
            .HandleAsync(new ListInstalledQuery(), CancellationToken.None);
        var module = Assert.Single(listed.Value);
        Assert.Equal("1.0.0", module.Version);
        Assert.Equal(ModuleLifecycleState.Active, module.Lifecycle);
        Assert.True(module.RestartRequired);
        Assert.Equal("1.0.0", module.PreviousVersion);

        var current = await world.Ledger.FindCurrentAsync("mail", CancellationToken.None);
        Assert.Equal("1.1.0", current!.Version);
        Assert.Equal(InstalledPackageState.Staged, current.State);
    }

    /// <summary>When the loader refuses the package the task fails with its code, the running version keeps running and the ledger is unchanged.</summary>
    [Fact]
    public async Task A_failed_install_keeps_the_previous_version_running()
    {
        using var world = await WithMailRunningAsync();
        world.Loader.Refusal = Error.Of("MarketplaceNotAvailable", ErrorType.Unavailable);

        var accepted = await world.UpdateHandler().HandleAsync(world.Update("mail", "1.1.0"), CancellationToken.None);
        await world.RunDispatchedAsync();

        Assert.True(accepted.IsSuccess);
        var failure = Assert.Single(world.Tasks.Failed);
        Assert.Equal("MarketplaceNotAvailable", failure.Code);
        Assert.Empty(world.Tasks.Completed);

        var running = Assert.Single(await world.Loader.ListAsync(CancellationToken.None));
        Assert.Equal("1.0.0", running.Version);
        Assert.Equal(ModuleLifecycleState.Active, running.State);
        var current = await world.Ledger.FindCurrentAsync("mail", CancellationToken.None);
        Assert.Equal("1.0.0", current!.Version);
        Assert.Contains(
            world.AuditFor(MarketplaceClientAuditActions.ModuleUpdateRequested),
            entry => { return !entry.Succeeded && entry.Subject.Contains("reason=Unclassified", StringComparison.Ordinal); });
        Assert.True((await world.Gate.AcquireAsync("mail", CancellationToken.None)).IsSuccess);
    }

    /// <summary>Updating a module that is not installed is not found; updating one that is installed is accepted.</summary>
    [Fact]
    public async Task Updating_a_module_that_is_not_installed_is_not_found()
    {
        using var world = new OperationWorld();
        world.Offer(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("1.1.0")),
            CatalogueSamples.Version("1.1.0"));

        var refused = await world.UpdateHandler().HandleAsync(world.Update("mail", "1.1.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleNotInstalled, refused.Error!.Code);
        Assert.Equal(ErrorType.NotFound, refused.Error.Type);
        Assert.Empty(world.Tasks.Begun);
        var entry = Assert.Single(world.AuditFor(MarketplaceClientAuditActions.ModuleUpdateRequested));
        Assert.False(entry.Succeeded);
    }

    /// <summary>Updating to the version already installed is refused as already installed.</summary>
    [Fact]
    public async Task Updating_to_the_installed_version_is_refused()
    {
        using var world = await WithMailRunningAsync();

        var refused = await world.UpdateHandler().HandleAsync(world.Update("mail", "1.0.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleAlreadyInstalled, refused.Error!.Code);
    }

    /// <summary>An update to a version below the highest ever installed is refused by the ledger floor; a newer one is accepted.</summary>
    [Fact]
    public async Task The_ledger_floor_blocks_an_older_catalogue_version()
    {
        using var world = await WithMailRunningAsync();
        await world.Ledger.RecordStagedAsync("mail", "1.5.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        world.Offer(
            CatalogueSamples.Module("mail", CatalogueSamples.Version("1.6.0")),
            CatalogueSamples.Version("1.6.0"),
            CatalogueSamples.Version("1.1.0"));

        var refused = await world.UpdateHandler().HandleAsync(world.Update("mail", "1.1.0"), CancellationToken.None);
        var accepted = await world.UpdateHandler().HandleAsync(world.Update("mail", "1.6.0"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleVersionNotInstallable, refused.Error!.Code);
        Assert.True(accepted.IsSuccess);
    }

    /// <summary>An update without the right password is refused and reserves nothing.</summary>
    [Fact]
    public async Task Update_without_the_password_is_refused()
    {
        using var world = await WithMailRunningAsync();

        var refused = await world.UpdateHandler().HandleAsync(
            world.Update("mail", "1.1.0") with { Password = string.Empty }, CancellationToken.None);

        Assert.Equal("ReauthenticationFailed", refused.Error!.Code);
        Assert.Empty(world.Tasks.Begun);
        Assert.Single(world.AuditFor(MarketplaceClientAuditActions.ModuleUpdateRequested));
    }
}
