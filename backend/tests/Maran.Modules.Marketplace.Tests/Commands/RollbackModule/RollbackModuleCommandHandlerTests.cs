using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.Commands.RollbackModule;

/// <summary>
/// Rolling back restores the version a module had before its last change, through the loader, and records
/// it as staged without lowering the floor; a module with nothing to go back to, or not installed, is refused.
/// </summary>
public sealed class RollbackModuleCommandHandlerTests
{
    /// <summary>The audit action every rollback request is journalled under.</summary>
    private const string Action = MarketplaceClientAuditActions.ModuleRollbackRequested;

    /// <summary>Builds a world with <c>mail</c> running 1.1.0, installed over 1.0.0.</summary>
    /// <returns>The world.</returns>
    private static async Task<OperationWorld> WithMailUpdatedAsync()
    {
        var world = new OperationWorld();
        await world.Ledger.RecordStagedAsync("mail", "1.0.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        await world.Ledger.RecordStagedAsync("mail", "1.1.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        world.Loader.Seed("mail", "1.1.0", ModuleLifecycleState.Active);
        return world;
    }

    /// <summary>A rollback goes through the loader, records the previous version as staged and leaves the floor where it was.</summary>
    [Fact]
    public async Task Rollback_restores_the_previous_version_and_keeps_the_floor()
    {
        using var world = await WithMailUpdatedAsync();

        var accepted = await world.RollbackHandler().HandleAsync(world.Rollback("mail"), CancellationToken.None);
        await world.RunDispatchedAsync();

        Assert.True(accepted.IsSuccess);
        Assert.Equal(TaskKinds.ModuleRollback, Assert.Single(world.Tasks.Begun).Kind);
        Assert.Equal("mail", Assert.Single(world.Loader.RolledBack));
        Assert.Single(world.Tasks.Completed);

        var current = await world.Ledger.FindCurrentAsync("mail", CancellationToken.None);
        Assert.Equal("1.0.0", current!.Version);
        Assert.Equal(InstalledPackageState.Staged, current.State);
        Assert.Equal("1.1.0", (await world.Ledger.ReadFloorAsync("mail", CancellationToken.None))!.ToString());
    }

    /// <summary>A rollback records the earlier package with the name that package was staged under, not the later one's.</summary>
    [Fact]
    public async Task Rollback_restores_the_name_the_earlier_package_was_staged_under()
    {
        using var world = new OperationWorld();
        await world.Ledger.RecordStagedAsync(
            "mail", "1.0.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None, "Mail");
        await world.Ledger.RecordStagedAsync(
            "mail", "1.1.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None, "Mail server");
        world.Loader.Seed("mail", "1.1.0", ModuleLifecycleState.Active);

        await world.RollbackHandler().HandleAsync(world.Rollback("mail"), CancellationToken.None);
        await world.RunDispatchedAsync();

        var current = await world.Ledger.FindCurrentAsync("mail", CancellationToken.None);
        Assert.Equal("1.0.0", current!.Version);
        Assert.Equal("Mail", current.DisplayName);
    }

    /// <summary>A module that was only ever installed once has nothing to go back to; the refusal is journalled and starts nothing.</summary>
    [Fact]
    public async Task A_module_with_no_previous_version_is_refused()
    {
        using var world = new OperationWorld();
        await world.Ledger.RecordStagedAsync("mail", "1.0.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Active);

        var refused = await world.RollbackHandler().HandleAsync(world.Rollback("mail"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleHasNoPreviousVersion, refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        Assert.Empty(world.Tasks.Begun);
        Assert.Contains("reason=ModuleHasNoPreviousVersion", Assert.Single(world.AuditFor(Action)).Subject, StringComparison.Ordinal);
    }

    /// <summary>A module that is not installed is not found.</summary>
    [Fact]
    public async Task Rolling_back_a_module_that_is_not_installed_is_not_found()
    {
        using var world = new OperationWorld();

        var refused = await world.RollbackHandler().HandleAsync(world.Rollback("mail"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleNotInstalled, refused.Error!.Code);
        Assert.Equal(ErrorType.NotFound, refused.Error.Type);
    }

    /// <summary>Rollback without the right password is refused and starts nothing.</summary>
    [Fact]
    public async Task Rollback_without_the_password_is_refused()
    {
        using var world = await WithMailUpdatedAsync();

        var refused = await world.RollbackHandler().HandleAsync(
            world.Rollback("mail") with { Password = string.Empty }, CancellationToken.None);

        Assert.Equal("ReauthenticationFailed", refused.Error!.Code);
        Assert.Empty(world.Loader.RolledBack);
        Assert.Empty(world.Tasks.Begun);
    }

    /// <summary>A rollback while a different operation holds the module is a conflict that reserves nothing.</summary>
    [Fact]
    public async Task A_rollback_during_another_operation_is_a_conflict()
    {
        using var world = await WithMailUpdatedAsync();
        await world.RemoveHandler().HandleAsync(world.Remove("mail"), CancellationToken.None);

        var refused = await world.RollbackHandler().HandleAsync(world.Rollback("mail"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleOperationInProgress, refused.Error!.Code);
        Assert.Single(world.Tasks.Begun);
    }

    /// <summary>If the loader refuses the rollback the task fails, the ledger is unchanged and the module is not locked.</summary>
    [Fact]
    public async Task A_loader_that_refuses_the_rollback_fails_the_task_and_changes_nothing()
    {
        using var world = await WithMailUpdatedAsync();
        world.Loader.Refusal = Error.Of("MarketplaceNotAvailable", ErrorType.Unavailable);

        await world.RollbackHandler().HandleAsync(world.Rollback("mail"), CancellationToken.None);
        await world.RunDispatchedAsync();

        Assert.Equal("MarketplaceNotAvailable", Assert.Single(world.Tasks.Failed).Code);
        Assert.Equal("1.1.0", (await world.Ledger.FindCurrentAsync("mail", CancellationToken.None))!.Version);
        Assert.True((await world.Gate.AcquireAsync("mail", CancellationToken.None)).IsSuccess);
    }
}
