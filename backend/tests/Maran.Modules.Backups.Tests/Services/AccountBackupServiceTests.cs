using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Services;

/// <summary>
/// The final backup taken before an account is destroyed: the row it writes, the kind it writes it
/// under, and — the load-bearing half — that a run which produced nothing answers a FAILURE the
/// caller can refuse a deletion on.
/// </summary>
public sealed class AccountBackupServiceTests
{
    /// <summary>The instant the injected clock reports, so nothing here reads the ambient clock.</summary>
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A terminal event describing a finished archive.</summary>
    private static readonly BackupCreateEvent Created =
        new(BackupCreateEventKind.Created, 100, "done", 8192, new string('c', 64), 2, null);

    /// <summary>A terminal event describing a run the agent could not finish.</summary>
    private static readonly BackupCreateEvent Dropped =
        new(BackupCreateEventKind.Dropped, 40, "archiving", 0, string.Empty, 0, null);

    /// <summary>A successful final backup writes a completed row of the pre-deletion kind.</summary>
    /// <remarks>
    /// The KIND is what exempts the row from the cascade about to run and from retention. Written
    /// under any other kind, the copy would be deleted by the very deletion it was taken for.
    /// </remarks>
    [Fact]
    public async Task A_successful_final_backup_writes_a_completed_pre_deletion_row()
    {
        var accountId = Guid.NewGuid();
        var world = World.Create(accountId, [Created]);

        var result = await world.Service.TakeFinalBackupAsync(accountId, "acme", CancellationToken.None);

        Assert.True(result.IsSuccess);

        await using var reading = world.Read();
        var stored = await reading.Backups.IgnoreQueryFilters().SingleAsync();

        Assert.Equal(result.Value, stored.Id);
        Assert.Equal(BackupKind.PreDeletion, stored.Kind);
        Assert.Equal(BackupStatus.Completed, stored.Status);
        Assert.Equal(accountId, stored.AccountId);
        Assert.Equal(new string('c', 64), stored.Sha256);
    }

    /// <summary>A run that produced nothing answers a failure, not a row the caller must inspect.</summary>
    /// <remarks>
    /// This is the difference from the customer's own create path, which deliberately answers success
    /// carrying a failed row because a screen has to list it either way. Here the caller is a deletion
    /// deciding whether to proceed, and the only thing it could do with "the row says Failed" is what
    /// a failed result already says — so folding it in is what makes the refusal impossible to forget
    /// at the call site.
    /// </remarks>
    [Fact]
    public async Task A_run_that_produced_nothing_answers_a_failure()
    {
        var accountId = Guid.NewGuid();
        var world = World.Create(accountId, [Dropped]);

        var result = await world.Service.TakeFinalBackupAsync(accountId, "acme", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("FinalBackupFailed", result.Error!.Code);
    }

    /// <summary>A failed final backup still leaves the row and the journal entry behind.</summary>
    /// <remarks>
    /// The record an operator needs in order to fix whatever refused and delete the account
    /// afterwards. A run that left nothing behind would present them with a deletion that fails for a
    /// reason nothing in the panel states.
    /// </remarks>
    [Fact]
    public async Task A_failed_final_backup_records_the_attempt_and_its_code()
    {
        var accountId = Guid.NewGuid();
        var world = World.Create(accountId, [Dropped]);

        await world.Service.TakeFinalBackupAsync(accountId, "acme", CancellationToken.None);

        await using var reading = world.Read();
        var stored = await reading.Backups.IgnoreQueryFilters().SingleAsync();

        Assert.Equal(BackupStatus.Failed, stored.Status);
        Assert.Equal(BackupKind.PreDeletion, stored.Kind);
        Assert.Equal("BackupStreamDropped", stored.FailureCode);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.FinalBackupTaken, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>The agent is asked for the name it was given, not for anything looked up here.</summary>
    [Fact]
    public async Task The_agent_is_asked_for_the_account_name_it_was_given()
    {
        var accountId = Guid.NewGuid();
        var world = World.Create(accountId, [Created]);

        var result = await world.Service.TakeFinalBackupAsync(accountId, "acme", CancellationToken.None);

        var create = Assert.Single(world.Agent.Creates);
        Assert.Equal("acme", create.AccountUsername);
        Assert.Equal(result.Value.ToString(), create.BackupId);
    }

    /// <summary>No panel task of its own is opened; the deletion's task is the one an operator watches.</summary>
    /// <remarks>
    /// Two tasks for one operation is two rows that can disagree. The runner is handed
    /// <see cref="Guid.Empty"/>, which <see cref="Sdk.Interfaces.ITaskRecorder"/> defines as "there
    /// is no task", so nothing here has to branch.
    /// </remarks>
    [Fact]
    public async Task No_task_of_its_own_is_opened()
    {
        var accountId = Guid.NewGuid();
        var world = World.Create(accountId, [Created]);

        await world.Service.TakeFinalBackupAsync(accountId, "acme", CancellationToken.None);

        Assert.Empty(world.Tasks.Tasks);
    }

    /// <summary>The service under test assembled over doubles.</summary>
    private sealed class World
    {
        /// <summary>The in-memory database the service and the reader share.</summary>
        private readonly string _database;

        /// <summary>The principal both contexts are bound to.</summary>
        private readonly FakeCurrentUser _currentUser;

        /// <summary>The service under test.</summary>
        public AccountBackupService Service { get; }

        /// <summary>The agent double, holding what it was asked for.</summary>
        public StubAgentBackupClient Agent { get; }

        /// <summary>The audit double, holding what was journalled.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>The task double, holding what was recorded.</summary>
        public RecordingTaskRecorder Tasks { get; }

        /// <summary>Assembles the service over doubles.</summary>
        /// <param name="accountId">The account being deleted.</param>
        /// <param name="events">The create stream the agent replays.</param>
        private World(Guid accountId, IReadOnlyList<BackupCreateEvent> events)
        {
            _database = Guid.NewGuid().ToString();

            // An administrator, because a deletion is an administrator's operation: the endpoint that
            // reaches this is AdminOnly at class level.
            _currentUser = FakeCurrentUser.Admin();

            Agent = new StubAgentBackupClient(events);
            Audit = new RecordingAuditWriter();
            Tasks = new RecordingTaskRecorder();

            var dbContext = BackupsTestContext.CreateSeeded(_currentUser, _database);

            Service = new AccountBackupService(
                dbContext,
                new BackupRunner(Agent, Tasks),
                new BackupDestinationResolver(dbContext),
                new BackupAuditJournal(Audit, _currentUser),
                new FakeClock(Now));
        }

        /// <summary>Assembles a world for one account.</summary>
        /// <param name="accountId">The account being deleted.</param>
        /// <param name="events">The create stream the agent replays.</param>
        /// <returns>The assembled world.</returns>
        public static World Create(Guid accountId, IReadOnlyList<BackupCreateEvent> events)
        {
            return new World(accountId, events);
        }

        /// <summary>Opens a second context over the same database, to read what was written.</summary>
        /// <returns>A context bound to the same principal and database.</returns>
        public BackupsDbContext Read()
        {
            return BackupsTestContext.Create(_currentUser, _database);
        }
    }
}
