using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Queries.ListInstalled;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Marketplace.Tests.Commands.RemoveModule;

/// <summary>
/// Removing a module unloads it and keeps its data; a module that is not installed is not found; the
/// password is required; and a concurrent operation is a conflict.
/// </summary>
public sealed class RemoveModuleCommandHandlerTests
{
    /// <summary>The audit action every remove request is journalled under.</summary>
    private const string Action = MarketplaceClientAuditActions.ModuleRemoveRequested;

    /// <summary>Builds a world with <c>mail</c> 1.0.0 installed through the ledger and running.</summary>
    /// <returns>The world.</returns>
    private static async Task<OperationWorld> WithMailRunningAsync()
    {
        var world = new OperationWorld();
        await world.Ledger.RecordStagedAsync("mail", "1.0.0", CatalogueSamples.Digest, world.UserId, CancellationToken.None);
        world.Loader.Seed("mail", "1.0.0", ModuleLifecycleState.Active);
        return world;
    }

    /// <summary>Removing unloads the module, closes its ledger history, keeps the version floor, and marks the module "data kept"; nothing asks the loader to delete data, because it has no such operation.</summary>
    [Fact]
    public async Task Remove_keeps_the_modules_data()
    {
        using var world = await WithMailRunningAsync();

        var accepted = await world.RemoveHandler().HandleAsync(world.Remove("mail"), CancellationToken.None);
        await world.RunDispatchedAsync();

        Assert.True(accepted.IsSuccess);
        Assert.Equal(TaskKinds.ModuleRemove, Assert.Single(world.Tasks.Begun).Kind);
        Assert.Equal("mail", Assert.Single(world.Loader.Unloaded));
        Assert.Single(world.Tasks.Completed);
        Assert.Null(await world.Ledger.FindCurrentAsync("mail", CancellationToken.None));
        Assert.Equal(
            InstalledPackageState.Removed,
            Assert.Single(await world.Ledger.ListHistoryAsync("mail", CancellationToken.None)).State);
        Assert.NotNull(await world.Ledger.ReadFloorAsync("mail", CancellationToken.None));

        var listed = await new ListInstalledQueryHandler(world.Installed, world.Names)
            .HandleAsync(new ListInstalledQuery(), CancellationToken.None);
        var module = Assert.Single(listed.Value);
        Assert.Equal(ModuleLifecycleState.Removed, module.Lifecycle);
        Assert.True(module.DataKept);
        Assert.False(module.CanRemove);
    }

    /// <summary>Removing a module that is not installed is the coded not-found refusal, journalled, and starts nothing; removing one that is installed is accepted.</summary>
    [Fact]
    public async Task Removing_a_module_that_is_not_installed_is_not_found()
    {
        using var world = await WithMailRunningAsync();

        var refused = await world.RemoveHandler().HandleAsync(world.Remove("dns"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleNotInstalled, refused.Error!.Code);
        Assert.Equal(ErrorType.NotFound, refused.Error.Type);
        Assert.Empty(world.Tasks.Begun);
        var entry = Assert.Single(world.AuditFor(Action));
        Assert.False(entry.Succeeded);
        Assert.Contains("reason=ModuleNotInstalled", entry.Subject, StringComparison.Ordinal);

        var accepted = await world.RemoveHandler().HandleAsync(world.Remove("mail"), CancellationToken.None);
        Assert.True(accepted.IsSuccess);
    }

    /// <summary>A module that was already removed is not installed any more, so removing it again is not found.</summary>
    [Fact]
    public async Task Removing_a_module_twice_is_not_found_the_second_time()
    {
        using var world = await WithMailRunningAsync();
        await world.RemoveHandler().HandleAsync(world.Remove("mail"), CancellationToken.None);
        await world.RunDispatchedAsync();

        var again = await world.RemoveHandler().HandleAsync(world.Remove("mail"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleNotInstalled, again.Error!.Code);
    }

    /// <summary>Remove without the right password is refused with the re-authentication failure and starts nothing.</summary>
    [Fact]
    public async Task Remove_without_the_password_is_refused()
    {
        using var world = await WithMailRunningAsync();

        var refused = await world.RemoveHandler().HandleAsync(
            world.Remove("mail") with { Password = "guess" }, CancellationToken.None);

        Assert.Equal("ReauthenticationFailed", refused.Error!.Code);
        Assert.Equal(1, world.Reauthentication.Refusals);
        Assert.Empty(world.Tasks.Begun);
        Assert.Empty(world.Loader.Unloaded);
        Assert.Contains("reason=ReauthenticationFailed", Assert.Single(world.AuditFor(Action)).Subject, StringComparison.Ordinal);
    }

    /// <summary>A second remove while the first runs answers with the first one's task and creates no second.</summary>
    [Fact]
    public async Task A_repeated_remove_shares_its_task()
    {
        using var world = await WithMailRunningAsync();

        var first = await world.RemoveHandler().HandleAsync(world.Remove("mail"), CancellationToken.None);
        var second = await world.RemoveHandler().HandleAsync(world.Remove("mail"), CancellationToken.None);

        Assert.Equal(first.Value.TaskId, second.Value.TaskId);
        Assert.Single(world.Tasks.Begun);
        Assert.Single(world.AuditFor(Action));
    }

    /// <summary>A remove retried against a restarted panel is still answered with the first one's task: the open operation is on the module's claim row.</summary>
    [Fact]
    public async Task A_repeated_remove_after_a_restart_shares_its_task()
    {
        using var world = await WithMailRunningAsync();

        var first = await world.RemoveHandler().HandleAsync(world.Remove("mail"), CancellationToken.None);
        var second = await world.RemoveHandlerAfterRestart().HandleAsync(world.Remove("mail"), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.TaskId, second.Value.TaskId);
        Assert.Single(world.Tasks.Begun);
        Assert.Single(world.AuditFor(Action));
    }

    /// <summary>A remove while another operation holds the module is a conflict that reserves nothing.</summary>
    [Fact]
    public async Task A_concurrent_operation_on_the_same_module_is_a_conflict()
    {
        using var world = await WithMailRunningAsync();
        await world.Gate.AcquireAsync("mail", CancellationToken.None);

        var refused = await world.RemoveHandler().HandleAsync(world.Remove("mail"), CancellationToken.None);

        Assert.Equal(ErrorCodes.ModuleOperationInProgress, refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        Assert.Empty(world.Tasks.Begun);
    }
}
