using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Commands.CreateBackup;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Extensions;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Modules.Backups.Tests.Commands.CreateBackup;

/// <summary>
/// What taking a backup writes: the row, the panel task and the audit entry, all from the one
/// outcome the run produced — and what it refuses, for an account the caller may not back up.
/// </summary>
public sealed class CreateBackupCommandHandlerTests
{
    /// <summary>The instant the injected clock reports, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    /// <summary>A terminal event describing a finished archive.</summary>
    private static readonly BackupCreateEvent Created =
        new(BackupCreateEventKind.Created, 100, "done", 4096, new string('b', 64), 3, null);

    /// <summary>A terminal event describing a run the agent could not finish.</summary>
    private static readonly BackupCreateEvent Dropped =
        new(BackupCreateEventKind.Dropped, 40, "archiving", 0, string.Empty, 0, null);

    /// <summary>A successful run writes a completed row carrying what the agent reported.</summary>
    [Fact]
    public async Task A_successful_run_writes_a_completed_row()
    {
        var accountId = Guid.NewGuid();
        var world = World.Owning(accountId, [Created]);

        var result = await world.Handler.HandleAsync(Command(accountId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BackupStatus.Completed, result.Value.Status);
        Assert.Equal(BackupKind.Manual, result.Value.Kind);
        Assert.Equal(4096, result.Value.SizeBytes);
        Assert.Equal(new string('b', 64), result.Value.Sha256);
        Assert.Equal(3, result.Value.DatabaseCount);
        Assert.Equal(string.Empty, result.Value.FailureCode);

        var stored = await world.Read().Backups.SingleAsync();
        Assert.Equal(BackupStatus.Completed, stored.Status);
        Assert.Equal(accountId, stored.AccountId);
        Assert.Equal(Now, stored.FinishedAt);
    }

    /// <summary>The stored row names the servers default destination, and the local call carries no path.</summary>
    /// <remarks>
    /// <para>
    /// Both halves matter and they used to be one lie each. The row's <c>DestinationId</c> was always
    /// null because no destination table existed; it now carries the identity the resolver answered
    /// with, which is what a later restore and a later deletion read to find the artifact.
    /// </para>
    /// <para>
    /// <b>The path assertion is inverted from what it used to be, and the old one certified a
    /// defect.</b> It asserted that the agent was handed the configured local root — and the agent
    /// refuses a local destination that carries a path at all, so every create, restore, delete and
    /// retention call this panel made would have been turned away by a real agent with
    /// <c>InvalidInput</c>. Nothing saw it because the client is stubbed here.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_stored_row_names_the_default_destination_and_the_local_call_carries_no_path()
    {
        var accountId = Guid.NewGuid();
        var world = World.Owning(accountId, [Created]);

        await world.Handler.HandleAsync(Command(accountId), CancellationToken.None);

        var stored = await world.Read().Backups.SingleAsync();
        Assert.Equal(BackupDestination.DefaultDestinationId, stored.DestinationId);

        var destination = Assert.Single(world.Agent.Destinations);
        Assert.Equal(AgentBackupDestinationKind.Local, destination.Kind);
        Assert.Equal(string.Empty, destination.Path);
    }

    /// <summary>The agent is asked for the owning accounts system user name and the rows own id.</summary>
    [Fact]
    public async Task The_agent_is_asked_for_the_owning_accounts_username_and_the_rows_own_id()
    {
        var accountId = Guid.NewGuid();
        var world = World.Owning(accountId, [Created]);

        var result = await world.Handler.HandleAsync(Command(accountId), CancellationToken.None);

        var call = Assert.Single(world.Agent.Creates);
        Assert.Equal(World.Username, call.AccountUsername);
        Assert.Equal(result.Value.Id.ToString(), call.BackupId);
    }

    /// <summary>A successful run journals the creation and completes the task.</summary>
    [Fact]
    public async Task A_successful_run_journals_the_creation_and_completes_the_task()
    {
        var accountId = Guid.NewGuid();
        var world = World.Owning(accountId, [Created]);

        var result = await world.Handler.HandleAsync(Command(accountId), CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.BackupCreated, entry.Action);
        Assert.Equal(result.Value.Id.ToString(), entry.Subject);
        Assert.True(entry.Succeeded);

        var task = Assert.Single(world.Tasks.Tasks);
        Assert.Equal(TaskKinds.BackupCreate, task.Kind);

        // The account, not the backup id. ITaskRecorder.BeginAsync asks for "what the operation acts
        // on, as an operator would search for it", and a task subject that is a bare UUID satisfies
        // neither half — it is what the tasks screen printed beside a restore that named the account.
        Assert.Equal(World.Username, task.Subject);
        Assert.NotEqual(result.Value.Id.ToString(), task.Subject);
        Assert.Equal(World.CorrelationId, task.CorrelationId);
        Assert.True(task.Completed);
        Assert.Null(task.FailureCode);
    }

    /// <summary>A run the agent could not finish is recorded as a failed row rather than an error.</summary>
    [Fact]
    public async Task A_run_the_agent_could_not_finish_is_recorded_as_a_failed_row()
    {
        var accountId = Guid.NewGuid();
        var world = World.Owning(accountId, [Dropped]);

        var result = await world.Handler.HandleAsync(Command(accountId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BackupStatus.Failed, result.Value.Status);
        Assert.Equal("BackupStreamDropped", result.Value.FailureCode);

        var stored = await world.Read().Backups.SingleAsync();
        Assert.Equal(BackupStatus.Failed, stored.Status);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.False(entry.Succeeded);

        var task = Assert.Single(world.Tasks.Tasks);
        Assert.False(task.Completed);
        Assert.Equal("BackupStreamDropped", task.FailureCode);
    }

    /// <summary>A stream that ends without a terminal event is a truncation and never a completion.</summary>
    [Fact]
    public async Task A_stream_that_ends_without_a_terminal_event_is_a_truncation()
    {
        var accountId = Guid.NewGuid();
        var world = World.Owning(accountId, []);

        var result = await world.Handler.HandleAsync(Command(accountId), CancellationToken.None);

        Assert.Equal(BackupStatus.Failed, result.Value.Status);
        Assert.Equal("BackupTruncated", result.Value.FailureCode);
    }

    /// <summary>Progress events reach the panel task as stages.</summary>
    [Fact]
    public async Task Progress_events_reach_the_panel_task_as_stages()
    {
        var accountId = Guid.NewGuid();
        var world = World.Owning(
            accountId,
            [
                new BackupCreateEvent(BackupCreateEventKind.Progress, 25, "home", 0, string.Empty, 0, null),
                new BackupCreateEvent(BackupCreateEventKind.Progress, 75, "databases", 0, string.Empty, 0, null),
                Created,
            ]);

        await world.Handler.HandleAsync(Command(accountId), CancellationToken.None);

        var task = Assert.Single(world.Tasks.Tasks);
        Assert.Equal([(25, "home"), (75, "databases")], task.Reports);
    }

    /// <summary>An account the caller does not own is not found rather than forbidden.</summary>
    /// <remarks>
    /// The IDOR case (rules/testing.md, Definition of Done 3). The directory answers null for an
    /// account outside the caller's tenant, and the refusal is asserted all the way to the HTTP
    /// status the panel would answer with — <c>404</c>, and specifically NOT <c>403</c>, which would
    /// confirm the account exists. Its inverse control is every accepting test above, which hands the
    /// same handler an account the directory does know and requires a backup back.
    /// </remarks>
    [Fact]
    public async Task An_account_the_caller_does_not_own_is_not_found_rather_than_forbidden()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        // The directory knows only the caller's own account, exactly as the tenant-scoped real one
        // answers for an account in another tenant.
        var world = World.Owning(mine, [Created]);

        var result = await world.Handler.HandleAsync(Command(theirs), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(StatusCodes.Status404NotFound, StatusOf(result));
        Assert.NotEqual(StatusCodes.Status403Forbidden, StatusOf(result));

        // Nothing was written and the agent was never reached: a refusal that had already created
        // the row would disclose the account's existence through the listing instead.
        Assert.Empty(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(world.Agent.Creates);
        Assert.Empty(world.Tasks.Tasks);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.BackupCreated, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>The refusal is decided by the directory rather than by a comparison in the handler.</summary>
    /// <remarks>
    /// The vacuity guard for the test above, on the axis that goes blind: if the handler stopped
    /// consulting the directory at all, "not found" could still be produced by some later step and
    /// the IDOR assertion would pass while the tenancy check had been deleted. This requires the
    /// lookup to have happened, for the id that was asked about.
    /// </remarks>
    [Fact]
    public async Task The_refusal_is_decided_by_asking_the_account_directory()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var world = World.Owning(mine, [Created]);

        await world.Handler.HandleAsync(Command(theirs), CancellationToken.None);

        Assert.Equal([theirs], world.Accounts.Lookups);
    }

    /// <summary>Builds a command naming <paramref name="accountId"/>.</summary>
    /// <param name="accountId">The account to back up.</param>
    /// <returns>The command.</returns>
    private static CreateBackupCommand Command(Guid accountId)
    {
        return new CreateBackupCommand(accountId, "203.0.113.7", "tests");
    }

    /// <summary>Runs a failed result through the panel's own translation and reports the status.</summary>
    /// <param name="result">The failed result to translate.</param>
    /// <returns>The HTTP status the panel would answer with.</returns>
    /// <remarks>
    /// Asserting the kind alone would be asserting an enum member; this asserts the answer the
    /// caller receives, produced by the same extension the controller calls.
    /// </remarks>
    private static int StatusOf<T>(Result<T> result)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };

        var actionResult = Assert.IsType<ObjectResult>(result.ToActionResult(httpContext));
        return actionResult.StatusCode ?? 0;
    }

    /// <summary>One assembled handler and every double it was built from.</summary>
    private sealed class World
    {
        /// <summary>The system user name the directory reports for the known account.</summary>
        public const string Username = "cust01";

        /// <summary>The correlation id the request carries.</summary>
        public const string CorrelationId = "corr-9";

        /// <summary>The in-memory database the handler and the reader share.</summary>
        private readonly string _database;

        /// <summary>The principal both contexts are bound to.</summary>
        private readonly FakeCurrentUser _currentUser;

        /// <summary>The handler under test.</summary>
        public CreateBackupCommandHandler Handler { get; }

        /// <summary>The agent double, holding what it was asked for.</summary>
        public StubAgentBackupClient Agent { get; }

        /// <summary>The audit double, holding what was journalled.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>The task double, holding what was recorded.</summary>
        public RecordingTaskRecorder Tasks { get; }

        /// <summary>The directory double, holding which accounts were looked up.</summary>
        public StubAccountDirectory Accounts { get; }

        /// <summary>Assembles the handler over doubles.</summary>
        /// <param name="ownedAccountId">The one account the directory knows, and the caller's own.</param>
        /// <param name="events">The create stream the agent replays.</param>
        private World(Guid ownedAccountId, IReadOnlyList<BackupCreateEvent> events)
        {
            _database = Guid.NewGuid().ToString();
            _currentUser = FakeCurrentUser.Customer(ownedAccountId);

            Agent = new StubAgentBackupClient(events);
            Audit = new RecordingAuditWriter();
            Tasks = new RecordingTaskRecorder();
            Accounts = new StubAccountDirectory(
                new AccountSnapshot(ownedAccountId, Username, 1, 1, 1, 1, 1, 1024));

            var dbContext = BackupsTestContext.CreateSeeded(_currentUser, _database);

            Handler = new CreateBackupCommandHandler(
                dbContext,
                Accounts,
                new BackupRunner(Agent, Tasks),
                new BackupDestinationResolver(dbContext),
                new BackupAuditJournal(Audit, _currentUser),
                new FakeClock(Now),
                Tasks,
                new StubCorrelationIdAccessor(CorrelationId),
                BackupsTestContext.FailureNames());
        }

        /// <summary>Assembles a world whose caller owns <paramref name="ownedAccountId"/>.</summary>
        /// <param name="ownedAccountId">The account the caller owns and the directory knows.</param>
        /// <param name="events">The create stream the agent replays.</param>
        /// <returns>The assembled world.</returns>
        public static World Owning(Guid ownedAccountId, IReadOnlyList<BackupCreateEvent> events)
        {
            return new World(ownedAccountId, events);
        }

        /// <summary>Opens a second context over the same database, to read what the handler wrote.</summary>
        /// <returns>A context bound to the same principal and database.</returns>
        public BackupsDbContext Read()
        {
            return BackupsTestContext.Create(_currentUser, _database);
        }
    }
}
