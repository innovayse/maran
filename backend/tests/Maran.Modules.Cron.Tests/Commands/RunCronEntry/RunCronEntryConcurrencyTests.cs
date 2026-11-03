using Maran.Agent.Client.Services.CronService;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.Commands.RunCronEntry;

/// <summary>The bound on runs in flight, and the audit that survives a caller who goes away (issues #333, #335).</summary>
public sealed class RunCronEntryConcurrencyTests
{
    private static readonly Guid OtherAccountId = Guid.Parse("cccccccc-0000-4000-8000-000000000003");

    /// <summary>A run past the account's bound is refused with the account's code before the agent is asked.</summary>
    [Fact]
    public async Task A_run_past_the_account_bound_is_refused_and_the_agent_is_not_asked_again()
    {
        var world = new RunCronEntryWorld(slots: RunCronEntryWorld.Bounded(perAccount: 1, global: 10));
        var release = new TaskCompletionSource();
        world.Agent.DuringRun = () =>
        {
            return release.Task;
        };

        var first = world.HandleAsync();
        var second = await world.HandleAsync();

        Assert.Equal("CronRunAccountLimitReached", second.Error!.Code);
        Assert.Equal(ErrorType.Conflict, second.Error.Type);
        Assert.Single(world.Agent.Runs);
        var refused = Assert.Single(world.Audit.Entries);
        Assert.False(refused.Succeeded);
        release.SetResult();
        Assert.True((await first).IsSuccess);
    }

    /// <summary>A run past the panel-wide bound is refused as server busy, whichever account asks.</summary>
    [Fact]
    public async Task A_run_past_the_global_bound_is_refused_as_server_busy()
    {
        var slots = RunCronEntryWorld.Bounded(perAccount: 5, global: 1);
        var busy = new RunCronEntryWorld(slots: slots);
        var other = new RunCronEntryWorld(slots: slots, ownerId: OtherAccountId);
        var release = new TaskCompletionSource();
        busy.Agent.DuringRun = () =>
        {
            return release.Task;
        };

        var first = busy.HandleAsync();
        var refused = await other.HandleAsync();

        Assert.Equal("CronRunServerBusy", refused.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, refused.Error.Type);
        Assert.Empty(other.Agent.Runs);
        release.SetResult();
        await first;
    }

    /// <summary>The bound counts runs in flight, so a finished run gives its slot back.</summary>
    [Fact]
    public async Task A_finished_run_gives_its_slot_back()
    {
        var world = new RunCronEntryWorld(slots: RunCronEntryWorld.Bounded(perAccount: 1, global: 1));

        var first = await world.HandleAsync();
        var second = await world.HandleAsync();

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(2, world.Agent.Runs.Count);
    }

    /// <summary>A run the agent refused gives its slot back as well.</summary>
    [Fact]
    public async Task A_run_the_agent_refused_gives_its_slot_back()
    {
        var world = new RunCronEntryWorld(slots: RunCronEntryWorld.Bounded(perAccount: 1, global: 1));
        world.Agent.RunEntryResult = Result<AgentCronRun>.Fail(
            Error.Of("AgentNotFound", ErrorType.NotFound));
        await world.HandleAsync();
        world.Agent.RunEntryResult = null;

        var second = await world.HandleAsync();

        Assert.True(second.IsSuccess);
    }

    /// <summary>A run whose agent call threw gives its slot back too.</summary>
    [Fact]
    public async Task A_run_whose_agent_call_threw_gives_its_slot_back()
    {
        var world = new RunCronEntryWorld(slots: RunCronEntryWorld.Bounded(perAccount: 1, global: 1));
        world.Agent.RunEntryThrows = new InvalidOperationException("transport fault");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return world.HandleAsync();
        });
        world.Agent.RunEntryThrows = null;

        var second = await world.HandleAsync();

        Assert.True(second.IsSuccess);
    }

    /// <summary>A caller who goes away after the agent answered still leaves the audit entry and the row.</summary>
    [Fact]
    public async Task A_caller_who_disconnects_after_the_run_still_leaves_the_audit_and_the_row()
    {
        var world = new RunCronEntryWorld();
        using var disconnect = new CancellationTokenSource();
        world.Agent.DuringRun = () =>
        {
            disconnect.Cancel();
            return Task.CompletedTask;
        };

        var result = await world.HandleAsync(cancellationToken: disconnect.Token);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.True(entry.Succeeded);
        Assert.Single(world.Database.CronRuns);
    }

    /// <summary>A caller who goes away while the agent holds the run leaves a failure entry and the cancellation travels on.</summary>
    [Fact]
    public async Task A_caller_who_disconnects_during_the_run_leaves_a_failure_entry()
    {
        var world = new RunCronEntryWorld();
        using var disconnect = new CancellationTokenSource();
        world.Agent.DuringRun = () =>
        {
            disconnect.Cancel();
            return Task.CompletedTask;
        };
        world.Agent.RunEntryThrows = new OperationCanceledException(disconnect.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
        {
            return world.HandleAsync(cancellationToken: disconnect.Token);
        });

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(CronAuditActions.CronEntryRun, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Empty(world.Database.CronRuns);
    }

    /// <summary>The audit entry is written before the history row, so a row that cannot be saved does not take it along.</summary>
    [Fact]
    public async Task The_audit_entry_is_written_before_the_history_row()
    {
        var world = new RunCronEntryWorld();
        var rowsWhenJournalled = -1;
        world.Audit.OnWrite = () =>
        {
            rowsWhenJournalled = world.Database.CronRuns.Count();
        };

        await world.HandleAsync();

        Assert.Equal(0, rowsWhenJournalled);
        Assert.Single(world.Audit.Entries);
        Assert.Single(world.Database.CronRuns);
    }

    /// <summary>Output carrying a NUL byte is stored without it and the run is still recorded and audited.</summary>
    [Fact]
    public async Task Output_with_a_nul_byte_is_stored_without_it_and_the_run_is_still_recorded()
    {
        var world = new RunCronEntryWorld();
        world.Agent.RunEntryResult = Result<AgentCronRun>.Ok(
            new AgentCronRun(
                5,
                new AgentCronRunExited(0),
                "be\0fore\0after",
                "err\0"));

        var result = await world.HandleAsync();

        Assert.True(result.IsSuccess);
        var row = Assert.Single(world.Database.CronRuns);
        Assert.Equal("beforeafter", row.Stdout);
        Assert.Equal("err", row.Stderr);
        Assert.Single(world.Audit.Entries);
    }
}
