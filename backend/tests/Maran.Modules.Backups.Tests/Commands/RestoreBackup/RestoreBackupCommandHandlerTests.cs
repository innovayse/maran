using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Commands.RestoreBackup;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Commands.RestoreBackup;

/// <summary>
/// What restoring an account does, and — more of this file — what it refuses before the agent is
/// asked for anything, because every refusal here is a refusal that has changed nothing.
/// </summary>
public sealed class RestoreBackupCommandHandlerTests
{
    /// <summary>The address every command in these tests is issued from.</summary>
    private const string Ip = "203.0.113.9";

    /// <summary>The user agent every command in these tests is issued with.</summary>
    private const string Client = "unit-tests";

    /// <summary>A terminal event describing a restore that replaced everything it set out to.</summary>
    private static readonly BackupRestoreEvent WholeRestore =
        new(BackupRestoreEventKind.Restored, 100, string.Empty, new AgentRestoreOutcome(true, 2, 2), null);

    /// <summary>A terminal event describing a restore that lost one of two databases.</summary>
    private static readonly BackupRestoreEvent PartialRestore =
        new(BackupRestoreEventKind.Restored, 100, string.Empty, new AgentRestoreOutcome(true, 1, 2), null);

    /// <summary>A whole restore answers success and reports what it replaced.</summary>
    [Fact]
    public async Task A_whole_restore_answers_success_and_reports_what_it_replaced()
    {
        var world = World.Create([WholeRestore]);

        var result = await world.Handler.HandleAsync(world.Command(World.Username), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Whole);
        Assert.True(result.Value.FilesRestored);
        Assert.Equal(2u, result.Value.DatabasesRestored);
        Assert.Equal(2u, result.Value.DatabasesTotal);
        Assert.Equal(string.Empty, result.Value.FailureCode);
    }

    /// <summary>A restore that lost a database is a failure, not a success carrying smaller numbers.</summary>
    /// <remarks>
    /// The pinned defect. The agent's outcome has no success field precisely so that the panel has to
    /// state the verdict, and the wrong statement — "the stream ended with a Restored event, so it
    /// worked" — leaves the account as a home from one moment over a database from another while the
    /// panel reports a clean restore.
    /// </remarks>
    [Fact]
    public async Task A_partial_restore_is_a_failure_and_the_task_records_it_as_one()
    {
        var world = World.Create([PartialRestore]);

        var result = await world.Handler.HandleAsync(world.Command(World.Username), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("RestorePartial", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);

        var task = Assert.Single(world.Tasks.Tasks);
        Assert.False(task.Completed);
        Assert.Equal("RestorePartial", task.FailureCode);
    }

    /// <summary>A stream that ended with no terminal event is never read as a completed restore.</summary>
    [Fact]
    public async Task A_stream_that_stated_no_outcome_is_recorded_as_truncated()
    {
        var world = World.Create([]);

        var result = await world.Handler.HandleAsync(world.Command(World.Username), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("RestoreTruncated", result.Error!.Code);
    }

    /// <summary>A Restored event carrying no outcome is refused rather than read as a restore of nothing.</summary>
    [Fact]
    public async Task A_restored_event_carrying_no_outcome_is_refused()
    {
        var world = World.Create([new BackupRestoreEvent(BackupRestoreEventKind.Restored, 100, string.Empty, null, null)]);

        var result = await world.Handler.HandleAsync(world.Command(World.Username), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("RestoreInvalidOutcome", result.Error!.Code);
    }

    /// <summary>
    /// An artifact the agent refused as unusable answers with the agent's OWN kind, so a corrupt
    /// copy is a 400 and not a 500.
    /// </summary>
    /// <remarks>
    /// The pinned defect, measured in a browser against a real agent: a tampered artifact produced
    /// an agent <c>ValidationFailed</c>, whose kind <c>AgentErrorTranslator.ToErrorType</c> already
    /// decided is <see cref="ErrorType.Validation"/>, and the handler rebuilt the error from its
    /// code alone with <see cref="ErrorType.Failure"/> hard-coded — so the code survived the journey
    /// and the kind did not, and the operator was told their server had broken. The assertion is on
    /// the VALUE of the kind: "some failure occurred" is true on both sides of this fix.
    /// </remarks>
    [Fact]
    public async Task An_artifact_the_agent_refused_answers_with_the_agents_own_kind()
    {
        var world = World.Create([
            new BackupRestoreEvent(
                BackupRestoreEventKind.Failed,
                0,
                string.Empty,
                null,
                Error.Of("AgentValidationFailed", ErrorType.Validation)),
        ]);

        var result = await world.Handler.HandleAsync(world.Command(World.Username), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("RestoreArtifactRejected", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    /// <summary>An ending the panel names itself keeps the kind the panel decided, not the agent's.</summary>
    /// <remarks>
    /// The other branch, and it is not decoration: a fix that carried the agent's kind everywhere
    /// would have nothing to carry for a stream that ended without an error at all, and a fix that
    /// merely renamed the hard-coded constant would pass the test above by accident. A truncated
    /// stream is the SERVER failing to finish, so it is <see cref="ErrorType.Failure"/> — a value
    /// this test states rather than bounds.
    /// </remarks>
    [Fact]
    public async Task A_truncated_stream_keeps_the_kind_the_panel_decided_for_it()
    {
        var world = World.Create([]);

        var result = await world.Handler.HandleAsync(world.Command(World.Username), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("RestoreTruncated", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
    }

    /// <summary>
    /// A refused confirmation and an unusable artifact are two different codes with two different
    /// kinds, so a screen can tell them apart.
    /// </summary>
    /// <remarks>
    /// This is the signal the interface branches on. Before the fix both endings reached the browser
    /// as one shape, and the dialog told an operator whose artifact was corrupt to correct their
    /// typing — advice that can never work. The two are distinguishable on the wire by code
    /// (<c>RestoreConfirmationMismatch</c> versus <c>RestoreArtifactRejected</c>) and by kind (409
    /// versus 400), and this asserts both at once so neither half can be collapsed again.
    /// </remarks>
    [Fact]
    public async Task A_refused_confirmation_and_an_unusable_artifact_are_distinguishable()
    {
        var refusedArtifact = World.Create([
            new BackupRestoreEvent(
                BackupRestoreEventKind.Failed,
                0,
                string.Empty,
                null,
                Error.Of("AgentValidationFailed", ErrorType.Validation)),
        ]);
        var wrongConfirmation = World.Create([WholeRestore]);

        var artifact = await refusedArtifact.Handler.HandleAsync(
            refusedArtifact.Command(World.Username), CancellationToken.None);
        var confirmation = await wrongConfirmation.Handler.HandleAsync(
            wrongConfirmation.Command("someone-else"), CancellationToken.None);

        Assert.Equal("RestoreConfirmationMismatch", confirmation.Error!.Code);
        Assert.Equal(ErrorType.Conflict, confirmation.Error.Type);
        Assert.Equal("RestoreArtifactRejected", artifact.Error!.Code);
        Assert.Equal(ErrorType.Validation, artifact.Error.Type);
        Assert.NotEqual(confirmation.Error.Code, artifact.Error.Code);
    }

    /// <summary>A confirmation that is not the accounts user name refuses before the agent is asked.</summary>
    [Fact]
    public async Task A_wrong_confirmation_refuses_and_the_agent_is_never_asked()
    {
        var world = World.Create([WholeRestore]);

        var result = await world.Handler.HandleAsync(world.Command("someone-else"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("RestoreConfirmationMismatch", result.Error!.Code);
        Assert.Empty(world.Agent.Restores);
        Assert.Empty(world.Tasks.Tasks);
    }

    /// <summary>The confirmation is compared case-sensitively, as Linux user names are.</summary>
    /// <remarks>
    /// The inverse control for the rule above: this proves the comparison refuses a value that
    /// differs ONLY in case, which an ordinal-ignore-case comparison would accept while every test
    /// using a plainly wrong name still passed.
    /// </remarks>
    [Fact]
    public async Task A_confirmation_differing_only_in_case_is_refused()
    {
        var world = World.Create([WholeRestore]);

        var result = await world.Handler.HandleAsync(world.Command(World.Username.ToUpperInvariant()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("RestoreConfirmationMismatch", result.Error!.Code);
        Assert.Empty(world.Agent.Restores);
    }

    /// <summary>A backup that is still being written cannot be restored from.</summary>
    [Fact]
    public async Task A_backup_that_never_completed_is_not_restorable()
    {
        var world = World.Create([WholeRestore], seedRunningBackup: true, restoreTheRunningOne: true);

        var result = await world.Handler.HandleAsync(world.Command(World.Username), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BackupNotRestorable", result.Error!.Code);
        Assert.Empty(world.Agent.Restores);
    }

    /// <summary>A create in flight for the account refuses the restore, in the panels own words.</summary>
    /// <remarks>
    /// The panel-side half of the concurrency answer. The agents per-account lock refuses it too, but
    /// this is the refusal the customer sees, and it is the only one of the two the panel can
    /// OBSERVE — a restore in flight writes no row, so nothing here can look for one.
    /// </remarks>
    [Fact]
    public async Task A_create_in_flight_for_the_account_refuses_the_restore()
    {
        var world = World.Create([WholeRestore], seedRunningBackup: true);

        var result = await world.Handler.HandleAsync(world.Command(World.Username), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BackupStillRunning", result.Error!.Code);
        Assert.Empty(world.Agent.Restores);
    }

    /// <summary>Another customers backup answers not-found, never forbidden.</summary>
    [Fact]
    public async Task Another_customers_backup_answers_not_found()
    {
        var world = World.Create([WholeRestore]);
        var stranger = new RestoreBackupCommand(Guid.NewGuid(), World.Username, Ip, Client);

        var result = await world.Handler.HandleAsync(stranger, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("BackupNotFound", result.Error.Code);
    }

    /// <summary>The agent is given the panels own digest and the panels own list of databases.</summary>
    /// <remarks>
    /// The two values the panel DECIDES, and neither is visible in the outcome — so without this
    /// assertion a restore aimed with the sidecars digest, or with the servers database list, would
    /// pass every other test in this file.
    /// </remarks>
    [Fact]
    public async Task The_agent_is_given_the_panels_digest_and_the_panels_database_list()
    {
        var world = World.Create([WholeRestore]);

        await world.Handler.HandleAsync(world.Command(World.Username), CancellationToken.None);

        var restore = Assert.Single(world.Agent.Restores);
        Assert.Equal(World.Username, restore.AccountUsername);
        Assert.Equal(world.BackupId.ToString(), restore.BackupId);
        Assert.Equal(new string('a', 64), restore.ExpectedSha256);
        Assert.Equal(["cust01_shop", "cust01_blog"], restore.AllowedDatabases);
    }

    /// <summary>A database the panel does not own is absent from what the agent is allowed to touch.</summary>
    /// <remarks>
    /// The directory answers with the panels rows, so a database on the server that no row names is
    /// simply not in the list — and the agent refuses, rather than creating, a manifest entry the
    /// list does not carry.
    /// </remarks>
    [Fact]
    public async Task A_database_the_panel_does_not_own_is_not_in_the_allowed_list()
    {
        var world = World.Create([WholeRestore]);

        await world.Handler.HandleAsync(world.Command(World.Username), CancellationToken.None);

        var restore = Assert.Single(world.Agent.Restores);
        Assert.DoesNotContain("cust01_forgotten", restore.AllowedDatabases);
    }

    /// <summary>The task carries the restore kind and the accounts name.</summary>
    [Fact]
    public async Task The_task_is_opened_under_the_restore_kind()
    {
        var world = World.Create([WholeRestore]);

        await world.Handler.HandleAsync(world.Command(World.Username), CancellationToken.None);

        var task = Assert.Single(world.Tasks.Tasks);
        Assert.Equal(TaskKinds.BackupRestore, task.Kind);
        Assert.Equal(World.Username, task.Subject);
        Assert.True(task.Completed);
    }

    /// <summary>Every ending, successful or not, is journalled under the restore action.</summary>
    [Fact]
    public async Task A_refused_restore_is_journalled_as_a_failure()
    {
        var world = World.Create([WholeRestore]);

        await world.Handler.HandleAsync(world.Command("someone-else"), CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.BackupRestored, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>The handler under test assembled over doubles, plus the seeded rows.</summary>
    private sealed class World
    {
        /// <summary>The system user name the directory reports for the account.</summary>
        public const string Username = "cust01";

        /// <summary>The in-memory database the handler and the reader share.</summary>
        private readonly string _database;

        /// <summary>The principal both contexts are bound to.</summary>
        private readonly FakeCurrentUser _currentUser;

        /// <summary>The handler under test.</summary>
        public RestoreBackupCommandHandler Handler { get; }

        /// <summary>The agent double, holding what it was asked for.</summary>
        public StubAgentBackupClient Agent { get; }

        /// <summary>The audit double, holding what was journalled.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>The task double, holding what was recorded.</summary>
        public RecordingTaskRecorder Tasks { get; }

        /// <summary>The backup the command names.</summary>
        public Guid BackupId { get; }

        /// <summary>Assembles the handler over doubles and seeds the rows.</summary>
        /// <param name="events">The restore stream the agent replays.</param>
        /// <param name="seedRunningBackup">Whether a create is in flight for the account.</param>
        /// <param name="restoreTheRunningOne">Whether the command names the running row instead.</param>
        private World(
            IReadOnlyList<BackupRestoreEvent> events,
            bool seedRunningBackup,
            bool restoreTheRunningOne)
        {
            var accountId = Guid.NewGuid();

            _database = Guid.NewGuid().ToString();
            _currentUser = FakeCurrentUser.Customer(accountId);

            Agent = new StubAgentBackupClient(restoreEvents: events);
            Audit = new RecordingAuditWriter();
            Tasks = new RecordingTaskRecorder();

            var completed = BackupsTestContext.CompletedRow(accountId);
            var running = BackupsTestContext.RunningRow(accountId);
            BackupId = restoreTheRunningOne ? running.Id : completed.Id;

            using (var seed = BackupsTestContext.Create(_currentUser, _database))
            {
                seed.Backups.Add(completed);
                if (seedRunningBackup)
                {
                    seed.Backups.Add(running);
                }

                seed.SaveChanges();
            }

            var dbContext = BackupsTestContext.CreateSeeded(_currentUser, _database);

            Handler = new RestoreBackupCommandHandler(
                dbContext,
                new StubAccountDirectory(new AccountSnapshot(accountId, Username, 1, 1, 1, 1, 1, 1024)),
                new StubAccountDatabaseDirectory(accountId, "cust01_shop", "cust01_blog"),
                new RestoreRunner(Agent, Tasks),
                new BackupDestinationResolver(dbContext),
                new BackupAuditJournal(Audit, _currentUser),
                Tasks,
                new StubCorrelationIdAccessor("corr-restore"));
        }

        /// <summary>Assembles a world replaying <paramref name="events"/>.</summary>
        /// <param name="events">The restore stream the agent replays.</param>
        /// <param name="seedRunningBackup">Whether a create is in flight for the account.</param>
        /// <param name="restoreTheRunningOne">Whether the command names the running row instead.</param>
        /// <returns>The assembled world.</returns>
        public static World Create(
            IReadOnlyList<BackupRestoreEvent> events,
            bool seedRunningBackup = false,
            bool restoreTheRunningOne = false)
        {
            return new World(events, seedRunningBackup, restoreTheRunningOne);
        }

        /// <summary>Builds the command naming this world's backup.</summary>
        /// <param name="confirmation">The account name the caller typed.</param>
        /// <returns>The command.</returns>
        public RestoreBackupCommand Command(string confirmation)
        {
            return new RestoreBackupCommand(BackupId, confirmation, Ip, Client);
        }

    }
}
