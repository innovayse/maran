using Maran.Modules.Marketplace.Domain.Entities;
using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Persistence;
using Maran.Modules.Marketplace.Services;
using Maran.Modules.Marketplace.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>At most one operation runs on a module at a time; different modules never wait on each other.</summary>
public sealed class ModuleOperationGateTests
{
    /// <summary>The clock both gates read, so staleness is decided by the test.</summary>
    private readonly AdvancingClock _clock = new();

    /// <summary>The name of the database both gates share.</summary>
    private readonly string _database = Guid.NewGuid().ToString("N");

    /// <summary>Builds a gate over the shared database, as a second request would be.</summary>
    /// <returns>A gate with its own context over the shared rows.</returns>
    private ModuleOperationGate NewGate()
    {
        return new ModuleOperationGate(MarketplaceDatabase.Create(_database), _clock);
    }

    /// <summary>The first acquire on a module succeeds and names the module.</summary>
    [Fact]
    public async Task The_first_acquire_on_a_module_succeeds()
    {
        var result = await NewGate().AcquireAsync("mail", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("mail", result.Value.ModuleId);
    }

    /// <summary>A second acquire on the same module is the coded conflict while the first is held.</summary>
    [Fact]
    public async Task A_second_acquire_on_the_same_module_is_a_conflict()
    {
        await NewGate().AcquireAsync("mail", CancellationToken.None);

        var second = await NewGate().AcquireAsync("mail", CancellationToken.None);

        Assert.False(second.IsSuccess);
        Assert.Equal("ModuleOperationInProgress", second.Error!.Code);
        Assert.Equal(ErrorType.Conflict, second.Error.Type);
    }

    /// <summary>A different module proceeds while another is held.</summary>
    [Fact]
    public async Task A_different_module_proceeds_while_another_is_held()
    {
        await NewGate().AcquireAsync("mail", CancellationToken.None);

        var other = await NewGate().AcquireAsync("dns", CancellationToken.None);

        Assert.True(other.IsSuccess);
    }

    /// <summary>Releasing frees the module for the next caller.</summary>
    [Fact]
    public async Task Releasing_frees_the_module()
    {
        var first = await NewGate().AcquireAsync("mail", CancellationToken.None);

        await NewGate().ReleaseAsync(first.Value, CancellationToken.None);

        Assert.True((await NewGate().AcquireAsync("mail", CancellationToken.None)).IsSuccess);
    }

    /// <summary>A claim that is not yet stale still blocks, an instant before the threshold.</summary>
    [Fact]
    public async Task A_claim_just_inside_the_threshold_still_blocks()
    {
        await NewGate().AcquireAsync("mail", CancellationToken.None);
        _clock.Advance(ModuleOperationLock.StaleAfter);

        var second = await NewGate().AcquireAsync("mail", CancellationToken.None);

        Assert.False(second.IsSuccess);
    }

    /// <summary>A stale claim is taken over by the next caller.</summary>
    [Fact]
    public async Task A_stale_claim_is_taken_over()
    {
        await NewGate().AcquireAsync("mail", CancellationToken.None);
        _clock.Advance(ModuleOperationLock.StaleAfter + TimeSpan.FromSeconds(1));

        var taken = await NewGate().AcquireAsync("mail", CancellationToken.None);

        Assert.True(taken.IsSuccess);
    }

    /// <summary>The old owner of a taken-over claim cannot release the new owner's claim.</summary>
    [Fact]
    public async Task The_old_owner_of_a_taken_over_claim_cannot_release_the_new_owners()
    {
        var old = await NewGate().AcquireAsync("mail", CancellationToken.None);
        _clock.Advance(ModuleOperationLock.StaleAfter + TimeSpan.FromSeconds(1));
        var taker = await NewGate().AcquireAsync("mail", CancellationToken.None);

        await NewGate().ReleaseAsync(old.Value, CancellationToken.None);

        var third = await NewGate().AcquireAsync("mail", CancellationToken.None);
        Assert.False(third.IsSuccess);
        await NewGate().ReleaseAsync(taker.Value, CancellationToken.None);
        Assert.True((await NewGate().AcquireAsync("mail", CancellationToken.None)).IsSuccess);
    }

    /// <summary>Releasing a claim that is already gone is not an error.</summary>
    [Fact]
    public async Task Releasing_a_claim_that_is_already_gone_is_not_an_error()
    {
        var claim = await NewGate().AcquireAsync("mail", CancellationToken.None);
        await NewGate().ReleaseAsync(claim.Value, CancellationToken.None);

        await NewGate().ReleaseAsync(claim.Value, CancellationToken.None);

        Assert.True((await NewGate().AcquireAsync("mail", CancellationToken.None)).IsSuccess);
    }

    /// <summary>
    /// A gate built afterwards over the same stored rows, which is what a restarted panel has, finds the
    /// task the first claim named. Nothing is shared between the two gates but the database.
    /// </summary>
    [Fact]
    public async Task A_gate_built_after_a_restart_finds_the_running_task()
    {
        var claim = await NewGate().AcquireAsync("mail", CancellationToken.None);
        var taskId = Guid.NewGuid();
        await NewGate().AttachTaskAsync(claim.Value, MarketplaceOperation.Install, "1.2.0", taskId, CancellationToken.None);

        var found = await NewGate().FindOpenAsync("mail", MarketplaceOperation.Install, "1.2.0", CancellationToken.None);

        Assert.Equal(taskId, found!.TaskId);
        Assert.Equal(MarketplaceOperation.Install, found.Operation);
        Assert.Equal("1.2.0", found.Version);
    }

    /// <summary>Only the same operation on the same version is a repeat; another operation, version or module is not.</summary>
    [Fact]
    public async Task Only_an_identical_operation_is_found()
    {
        var claim = await NewGate().AcquireAsync("mail", CancellationToken.None);
        await NewGate().AttachTaskAsync(claim.Value, MarketplaceOperation.Install, "1.2.0", Guid.NewGuid(), CancellationToken.None);

        Assert.Null(await NewGate().FindOpenAsync("mail", MarketplaceOperation.Update, "1.2.0", CancellationToken.None));
        Assert.Null(await NewGate().FindOpenAsync("mail", MarketplaceOperation.Install, "1.3.0", CancellationToken.None));
        Assert.Null(await NewGate().FindOpenAsync("dns", MarketplaceOperation.Install, "1.2.0", CancellationToken.None));
        Assert.NotNull(await NewGate().FindOpenAsync("mail", MarketplaceOperation.Install, "1.2.0", CancellationToken.None));
    }

    /// <summary>An operation that names no version (a remove) is found by asking with the same version, null or not.</summary>
    [Fact]
    public async Task An_operation_with_no_version_is_found_by_asking_without_one()
    {
        var claim = await NewGate().AcquireAsync("mail", CancellationToken.None);
        await NewGate().AttachTaskAsync(claim.Value, MarketplaceOperation.Remove, null, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(await NewGate().FindOpenAsync("mail", MarketplaceOperation.Remove, null, CancellationToken.None));
        Assert.Null(await NewGate().FindOpenAsync("mail", MarketplaceOperation.Remove, "1.0.0", CancellationToken.None));
    }

    /// <summary>A claim whose task has not been named yet is no repeat of anything: the caller meets the conflict.</summary>
    [Fact]
    public async Task A_claim_without_a_task_is_not_a_repeat()
    {
        await NewGate().AcquireAsync("mail", CancellationToken.None);

        Assert.Null(await NewGate().FindOpenAsync("mail", MarketplaceOperation.Install, "1.2.0", CancellationToken.None));
    }

    /// <summary>A stale claim and a released claim are not open operations.</summary>
    [Fact]
    public async Task A_stale_or_released_claim_is_not_open()
    {
        var claim = await NewGate().AcquireAsync("mail", CancellationToken.None);
        await NewGate().AttachTaskAsync(claim.Value, MarketplaceOperation.Install, "1.2.0", Guid.NewGuid(), CancellationToken.None);

        _clock.Advance(ModuleOperationLock.StaleAfter + TimeSpan.FromSeconds(1));
        Assert.Null(await NewGate().FindOpenAsync("mail", MarketplaceOperation.Install, "1.2.0", CancellationToken.None));

        var fresh = await NewGate().AcquireAsync("mail", CancellationToken.None);
        await NewGate().AttachTaskAsync(fresh.Value, MarketplaceOperation.Install, "1.2.0", Guid.NewGuid(), CancellationToken.None);
        Assert.NotNull(await NewGate().FindOpenAsync("mail", MarketplaceOperation.Install, "1.2.0", CancellationToken.None));

        await NewGate().ReleaseAsync(fresh.Value, CancellationToken.None);
        Assert.Null(await NewGate().FindOpenAsync("mail", MarketplaceOperation.Install, "1.2.0", CancellationToken.None));
    }

    /// <summary>Naming the task on a claim that was taken over reports false and leaves the new owner's claim alone.</summary>
    [Fact]
    public async Task Naming_a_task_on_a_taken_over_claim_changes_nothing()
    {
        var old = await NewGate().AcquireAsync("mail", CancellationToken.None);
        _clock.Advance(ModuleOperationLock.StaleAfter + TimeSpan.FromSeconds(1));
        await NewGate().AcquireAsync("mail", CancellationToken.None);

        var attached = await NewGate()
            .AttachTaskAsync(old.Value, MarketplaceOperation.Install, "1.2.0", Guid.NewGuid(), CancellationToken.None);

        Assert.False(attached);
        Assert.Null(await NewGate().FindOpenAsync("mail", MarketplaceOperation.Install, "1.2.0", CancellationToken.None));
    }

    /// <summary>The database refuses a second claim on one module: the unique index is the arbiter the code relies on.</summary>
    [Fact]
    public void The_database_model_holds_a_unique_index_on_the_module_id()
    {
        using var context = MarketplaceDatabase.CreateIsolated();

        var claims = context.Model.FindEntityType(typeof(ModuleOperationLock))!;

        var index = Assert.Single(claims.GetIndexes());
        Assert.True(index.IsUnique);
        Assert.Equal(["ModuleId"], index.Properties.Select(property => { return property.Name; }));
        Assert.Equal("ModuleOperationLocks", claims.GetTableName());
        Assert.Equal(MarketplaceDbContext.SchemaName, claims.GetSchema());
    }
}
