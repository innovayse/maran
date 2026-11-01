using Maran.Agent.Client.Services.CronService;
using Maran.Modules.Cron.Commands.RunCronEntry;
using Maran.Modules.Cron.Common;
using Maran.Modules.Cron.Domain.Entities;
using Maran.Modules.Cron.Domain.Enums;
using Maran.Modules.Cron.Domain.Policies;
using Maran.Modules.Cron.Persistence;
using Maran.Modules.Cron.Services;
using Maran.Modules.Cron.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Cron.Tests.Commands.RunCronEntry;

/// <summary>What running an entry by hand asks of the agent, what it records, and what it refuses.</summary>
public sealed class RunCronEntryCommandHandlerTests
{
    private const string EntryId = "3f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";

    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>An account the caller may not see is answered not found and the agent is never asked.</summary>
    [Fact]
    public async Task An_account_the_caller_may_not_see_is_not_found_and_nothing_runs()
    {
        var world = new World();

        var result = await world.HandleAsync(accountId: StrangerAccountId);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(world.Agent.Runs);
        Assert.Empty(world.Database.CronRuns);
    }

    /// <summary>A suspended account is refused before the agent is asked.</summary>
    [Fact]
    public async Task A_suspended_account_is_refused_before_the_agent_is_asked()
    {
        var world = new World(AccountState.Suspended);

        var result = await world.HandleAsync();

        Assert.Equal("CronEntrySuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(world.Agent.Runs);
    }

    /// <summary>A run addresses the agent by the account's system user name and records what it reported.</summary>
    [Fact]
    public async Task A_run_addresses_the_agent_and_records_what_it_reported()
    {
        var world = new World();

        var result = await world.HandleAsync();

        var call = Assert.Single(world.Agent.Runs);
        Assert.Equal("alice", call.AccountUsername);
        Assert.Equal(EntryId, call.EntryId);
        var row = Assert.Single(world.Database.CronRuns);
        Assert.Equal(world.Clock.UtcNow, row.StartedAt);
        Assert.Equal(1200, row.DurationMilliseconds);
        Assert.Equal(CronRunOutcome.Succeeded, row.Outcome);
        Assert.Equal(row.Id, result.Value.Id);
        Assert.Equal("done\n", result.Value.Stdout);
    }

    /// <summary>A job that failed is a recorded run, not a failed request.</summary>
    [Fact]
    public async Task A_job_that_failed_is_a_recorded_run_not_a_failed_request()
    {
        var world = new World();
        world.Agent.RunEntryResult = Result<AgentCronRun>.Ok(new AgentCronRun(40, 1, string.Empty, "boom", null));

        var result = await world.HandleAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(CronRunOutcome.Failed, result.Value.Outcome);
        Assert.Equal(1, result.Value.ExitCode);
    }

    /// <summary>A run a ceiling stopped is recorded as stopped, with its ceiling and no status.</summary>
    [Fact]
    public async Task A_run_a_ceiling_stopped_is_recorded_as_stopped()
    {
        var world = new World();
        world.Agent.RunEntryResult = Result<AgentCronRun>.Ok(
            new AgentCronRun(120_000, null, string.Empty, string.Empty, AgentCronRunLimit.Runtime));

        var result = await world.HandleAsync();

        Assert.Equal(CronRunOutcome.Stopped, result.Value.Outcome);
        Assert.Equal(CronRunLimit.Runtime, result.Value.Limit);
        Assert.Null(result.Value.ExitCode);
    }

    /// <summary>An agent refusal records nothing, maps to the module's code and is journalled as a failure.</summary>
    [Fact]
    public async Task An_agent_refusal_records_nothing_and_is_journalled_as_a_failure()
    {
        var world = new World();
        world.Agent.RunEntryResult = Result<AgentCronRun>.Fail(Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await world.HandleAsync();

        Assert.Equal("CronEntryNotFound", result.Error!.Code);
        Assert.Empty(world.Database.CronRuns);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CronEntryRun, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A run is journalled with the entry id and never with what it printed.</summary>
    [Fact]
    public async Task A_run_is_journalled_with_the_entry_id_and_never_its_output()
    {
        var world = new World();

        await world.HandleAsync();

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.CronEntryRun, entry.Action);
        Assert.Equal(EntryId, entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain("done", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>The history keeps only the newest runs of the entry, and other entries' runs are untouched.</summary>
    [Fact]
    public async Task The_history_keeps_only_the_newest_runs_of_the_entry()
    {
        var world = new World();
        const string OtherEntry = "9f1a5b7c-0d2e-4a6b-8c9d-0e1f2a3b4c5d";
        for (var minute = 0; minute < CronRunRetention.RunsPerEntry; minute++)
        {
            world.Database.CronRuns.Add(Run(EntryId, world.Clock.UtcNow.AddMinutes(-60 + minute)));
        }

        world.Database.CronRuns.Add(Run(OtherEntry, world.Clock.UtcNow.AddDays(-1)));
        await world.Database.SaveChangesAsync();

        await world.HandleAsync();

        var kept = world.Database.CronRuns.Where(run => run.EntryId == EntryId).ToList();
        Assert.Equal(CronRunRetention.RunsPerEntry, kept.Count);
        Assert.DoesNotContain(kept, run => run.StartedAt == world.Clock.UtcNow.AddMinutes(-60));
        Assert.Contains(kept, run => run.StartedAt == world.Clock.UtcNow);
        Assert.Single(world.Database.CronRuns, run => run.EntryId == OtherEntry);
    }

    /// <summary>A recorded run of <paramref name="entryId"/> at <paramref name="startedAt"/>.</summary>
    /// <param name="entryId">The entry.</param>
    /// <param name="startedAt">When it started.</param>
    /// <returns>The run.</returns>
    private static CronRun Run(string entryId, DateTimeOffset startedAt)
    {
        return new CronRun(Guid.NewGuid(), AccountId, entryId, startedAt, 10, 0, null, "ok", string.Empty);
    }

    /// <summary>Everything one run test needs, wired the way the Host wires it.</summary>
    private sealed class World
    {
        /// <summary>The agent double every call is recorded on.</summary>
        public RecordingAgentCronClient Agent { get; } = new();

        /// <summary>The journal every entry lands in.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>The panel's clock, fixed.</summary>
        public FixedClock Clock { get; } = new();

        /// <summary>The module's table.</summary>
        public CronDbContext Database { get; }

        /// <summary>The handler under test.</summary>
        public RunCronEntryCommandHandler Handler { get; }

        /// <summary>Wires one world whose account is in <paramref name="state"/>.</summary>
        /// <param name="state">The owning account's state.</param>
        public World(AccountState state = AccountState.Active)
        {
            var currentUser = FakeCurrentUser.Customer(AccountId);
            Database = InMemoryCronDatabase.Open(Guid.NewGuid().ToString(), currentUser);
            Handler = new RunCronEntryCommandHandler(
                new StubAccountDirectory(new AccountSnapshot(AccountId, "alice", 5, 5, 5, 5, 5, 1_024, state)),
                Agent,
                Database,
                Clock,
                new CronAuditJournal(Audit, currentUser),
                new CapturingLogger<RunCronEntryCommandHandler>());
        }

        /// <summary>Runs the handler with the usual audit context.</summary>
        /// <param name="accountId">The account to act under; the owner by default.</param>
        /// <returns>What the handler answered.</returns>
        public async Task<Result<CronRunDto>> HandleAsync(Guid? accountId = null)
        {
            return await Handler.HandleAsync(
                new RunCronEntryCommand(accountId ?? AccountId, EntryId, "203.0.113.7", "tests"),
                CancellationToken.None);
        }
    }
}
