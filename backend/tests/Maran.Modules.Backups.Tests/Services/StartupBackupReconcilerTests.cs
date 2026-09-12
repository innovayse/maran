using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Backups.Tests.Services;

/// <summary>
/// What the startup reclamation does to a backup row the previous process left running: it asks the
/// destination what is really there, closes the row on the answer, and leaves alone both the rows it
/// could not ask about and the archives it found.
/// </summary>
public sealed class StartupBackupReconcilerTests
{
    /// <summary>The instant the reconciler's clock reports; also the process-start boundary.</summary>
    private static readonly DateTimeOffset Now = new(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);

    /// <summary>An instant before the boundary: a row a previous process opened.</summary>
    private static readonly DateTimeOffset BeforeBoundary = Now.AddHours(-2);

    /// <summary>An instant after the boundary: a row THIS process opened, which must be spared.</summary>
    private static readonly DateTimeOffset AfterBoundary = Now.AddSeconds(5);

    /// <summary>The account every fixture here backs up.</summary>
    private static readonly Guid AccountId = Guid.NewGuid();

    /// <summary>The account's system user name, which is what the agent call is addressed by.</summary>
    private const string Username = "acme";

    /// <summary>A row left running by a dead process, with no archive on the destination, is failed.</summary>
    /// <remarks>
    /// The whole point of the pass. Before it existed such a row stayed Running for ever, and a
    /// Running row refuses every restore of that account — so one lost stream cost the customer the
    /// ability to restore from any of their GOOD backups.
    /// </remarks>
    [Fact]
    public async Task A_row_with_no_artifact_on_the_destination_is_failed_as_unobserved()
    {
        var world = World.WithRunningBackup(BeforeBoundary);

        var reclamation = await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(new Models.BackupReclamation(1, 0, 1, 0), reclamation);

        var stored = await world.Read().Backups.SingleAsync();
        Assert.Equal(BackupStatus.Failed, stored.Status);
        Assert.Equal("BackupOutcomeUnobserved", stored.FailureCode);
        Assert.Equal(Now, stored.FinishedAt);
    }

    /// <summary>
    /// A row whose archive IS on the destination is failed too — never completed — and the archive is
    /// not deleted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The listing carries the artifact's size and its SHA-256, so the row COULD be completed from it.
    /// It must not be: <c>Backup.Sha256</c> is the panel's independent digest precisely so a restore
    /// does not trust one read from beside the bytes it describes, and a row completed here would be
    /// indistinguishable from one the panel observed. The distinct code is what tells an operator
    /// there are bytes on the disk to release.
    /// </para>
    /// <para>
    /// The digest and size assertions are the load-bearing half: they assert the row did NOT take the
    /// listing's values, which is the only way to tell this arm from a completion.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_row_whose_archive_exists_is_failed_as_unvouched_and_the_archive_is_kept()
    {
        var world = World.WithRunningBackup(BeforeBoundary);
        world.Agent.ListResult = Result<IReadOnlyList<AgentBackupSummary>>.Ok(
            [Readable(world.BackupId)]);

        var reclamation = await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(new Models.BackupReclamation(1, 1, 0, 0), reclamation);

        var stored = await world.Read().Backups.SingleAsync();
        Assert.Equal(BackupStatus.Failed, stored.Status);
        Assert.Equal("BackupArtifactUnvouched", stored.FailureCode);
        Assert.Equal(string.Empty, stored.Sha256);
        Assert.Equal(0, stored.SizeBytes);
        Assert.Empty(world.Agent.Deletes);
    }

    /// <summary>
    /// An entry the agent lists as UNREADABLE is not treated as a finished archive.
    /// </summary>
    /// <remarks>
    /// The inverse control for the test above, on the axis that would otherwise go vacuous: a pass
    /// that counted any listed entry as present would pass both, and the difference is exactly the one
    /// the agent went to the trouble of expressing. An unreadable entry is an artifact the agent
    /// declined to describe, and believing it anyway would be the panel asserting the one thing it was
    /// told nobody could say.
    /// </remarks>
    [Fact]
    public async Task An_unreadable_entry_is_not_read_as_a_finished_archive()
    {
        var world = World.WithRunningBackup(BeforeBoundary);
        world.Agent.ListResult = Result<IReadOnlyList<AgentBackupSummary>>.Ok(
        [
            new AgentBackupSummary(
                world.BackupId.ToString(),
                null,
                new AgentBackupUnreadableReason(AgentBackupUnreadableKind.Corrupt, 1)),
        ]);

        var reclamation = await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(new Models.BackupReclamation(1, 0, 1, 0), reclamation);
        Assert.Equal("BackupOutcomeUnobserved", (await world.Read().Backups.SingleAsync()).FailureCode);
    }

    /// <summary>An archive belonging to a DIFFERENT backup does not reclaim this row.</summary>
    /// <remarks>
    /// The other way the presence check can be vacuous: a pass that asked "does the destination hold
    /// anything at all" would answer yes for an account with ten older backups and would report every
    /// lost run as having produced an archive. The entry is matched by the row's own id or not at all.
    /// </remarks>
    [Fact]
    public async Task An_archive_under_another_backups_id_does_not_count_as_this_rows()
    {
        var world = World.WithRunningBackup(BeforeBoundary);
        world.Agent.ListResult = Result<IReadOnlyList<AgentBackupSummary>>.Ok(
            [Readable(Guid.NewGuid())]);

        var reclamation = await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(new Models.BackupReclamation(1, 0, 1, 0), reclamation);
    }

    /// <summary>A row this process opened is left strictly alone.</summary>
    /// <remarks>
    /// The boundary is what makes the pass safe to run late: a hosted service starts alongside the web
    /// server, not strictly before it, so a backup opened by the first request to arrive can be in the
    /// table while this is reading it. Closing it would be the very failure the class exists to fix,
    /// caused by the fix. The agent is not even asked about it — asserted, because a pass that asked
    /// and then spared it would be one refactor away from closing it.
    /// </remarks>
    [Fact]
    public async Task A_row_this_process_opened_is_neither_examined_nor_closed()
    {
        var world = World.WithRunningBackup(AfterBoundary);

        var reclamation = await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(new Models.BackupReclamation(0, 0, 0, 0), reclamation);
        Assert.Empty(world.Agent.Listings);
        Assert.Equal(BackupStatus.Running, (await world.Read().Backups.SingleAsync()).Status);
    }

    /// <summary>A completed backup is not touched, and the agent is not asked about it.</summary>
    /// <remarks>
    /// The inverse control rules/testing.md requires of a pass that closes rows: the candidate
    /// predicate is a status AND a boundary, and a pass that had lost the status half would fail every
    /// good backup on the host at the next restart — the single worst thing this code could do.
    /// </remarks>
    [Fact]
    public async Task A_completed_backup_is_left_exactly_as_it_was()
    {
        var world = World.WithCompletedBackup(BeforeBoundary);

        var reclamation = await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(new Models.BackupReclamation(0, 0, 0, 0), reclamation);
        Assert.Empty(world.Agent.Listings);

        var stored = await world.Read().Backups.SingleAsync();
        Assert.Equal(BackupStatus.Completed, stored.Status);
        Assert.Equal(new string('a', 64), stored.Sha256);
        Assert.Equal(string.Empty, stored.FailureCode);
    }

    /// <summary>A row the destination could not be asked about is LEFT RUNNING and counted.</summary>
    /// <remarks>
    /// This is the preference for observation taken seriously. A pass that closed a row because the
    /// agent was unreachable would be inferring from silence, which is the one thing it was written not
    /// to do — and the row it closed might have an archive nobody would then be told about. The cost is
    /// that the row stays stuck until a start at which the agent answers, and the count is what makes
    /// that visible instead of silent.
    /// </remarks>
    [Fact]
    public async Task A_row_whose_destination_cannot_be_asked_is_left_running()
    {
        var world = World.WithRunningBackup(BeforeBoundary);
        world.Agent.ListResult = Result<IReadOnlyList<AgentBackupSummary>>.Fail(
            Error.Of("AgentUnspecified", ErrorType.Failure));

        var reclamation = await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(new Models.BackupReclamation(1, 0, 0, 1), reclamation);
        Assert.Equal(BackupStatus.Running, (await world.Read().Backups.SingleAsync()).Status);
    }

    /// <summary>The agent is asked for the owning account's system user name and its destination.</summary>
    /// <remarks>
    /// Neither is visible in the outcome, and both are what the pass DECIDES: a listing aimed at the
    /// wrong account reads another customer's directory, and one aimed at the wrong destination answers
    /// about bytes that are not these.
    /// </remarks>
    [Fact]
    public async Task The_listing_is_aimed_at_the_owning_account_and_the_rows_destination()
    {
        var world = World.WithRunningBackup(BeforeBoundary);

        await world.Reconciler.ReconcileAsync(CancellationToken.None);

        var listing = Assert.Single(world.Agent.Listings);
        Assert.Equal(Username, listing.AccountUsername);
        Assert.Equal(AgentBackupDestinationKind.Local, listing.Destination.Kind);
        Assert.Equal(string.Empty, listing.Destination.Path);
    }

    /// <summary>Every row the pass closes is journalled as a failed backup, naming the account.</summary>
    /// <remarks>
    /// The subject is the account's system user name, which is what an operator searches the journal
    /// for and what every other backup entry records. The pass has no signed-in caller, so the entry
    /// names the panel as the actor through the journal's unattended path.
    /// </remarks>
    [Fact]
    public async Task A_reclaimed_row_is_journalled_against_the_account()
    {
        var world = World.WithRunningBackup(BeforeBoundary);

        await world.Reconciler.ReconcileAsync(CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.BackupCreated, entry.Action);
        Assert.Equal(Username, entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A pass over a database with nothing to reclaim writes nothing and asks nothing.</summary>
    [Fact]
    public async Task A_pass_with_no_candidates_asks_the_agent_nothing()
    {
        var world = World.Empty();

        var reclamation = await world.Reconciler.ReconcileAsync(CancellationToken.None);

        Assert.Equal(new Models.BackupReclamation(0, 0, 0, 0), reclamation);
        Assert.Empty(world.Agent.Listings);
        Assert.Empty(world.Audit.Entries);
    }

    /// <summary>Builds a readable listing entry for <paramref name="backupId"/>.</summary>
    /// <param name="backupId">The backup the entry describes.</param>
    /// <returns>An entry whose <c>Readable</c> arm carries a size and a digest.</returns>
    /// <remarks>
    /// The size and digest are deliberately NOT the values any row here holds, so a test asserting the
    /// row did not take them cannot pass by coincidence.
    /// </remarks>
    private static AgentBackupSummary Readable(Guid backupId)
    {
        return new AgentBackupSummary(
            backupId.ToString(),
            new AgentReadableBackup(
                new AgentBackupManifest(1, Username, backupId.ToString(), 0, 1024, [], "0.1.0"),
                9999,
                new string('f', 64)),
            null);
    }

    /// <summary>One reconciler, its database and the doubles it decides against.</summary>
    private sealed class World
    {
        /// <summary>The name of the in-memory database, so a second context can read it back.</summary>
        private readonly string _databaseName;

        /// <summary>The principal the context's tenant filter closes over.</summary>
        private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Admin();

        /// <summary>The pass under test.</summary>
        public StartupBackupReconciler Reconciler { get; private set; } = null!;

        /// <summary>The agent double, which answers what the destination holds.</summary>
        public StubAgentBackupClient Agent { get; } = new();

        /// <summary>The journal double.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>The identity of the one backup row the fixture seeded.</summary>
        public Guid BackupId { get; private set; }

        /// <summary>Creates a world over a fresh, destination-seeded database.</summary>
        private World()
        {
            _databaseName = Guid.NewGuid().ToString();
        }

        /// <summary>A world whose database holds no backup at all.</summary>
        /// <returns>The world.</returns>
        public static World Empty()
        {
            var world = new World();
            world.Build(seed: null);
            return world;
        }

        /// <summary>A world holding one running backup started at <paramref name="startedAt"/>.</summary>
        /// <param name="startedAt">When the run began, relative to the process-start boundary.</param>
        /// <returns>The world.</returns>
        public static World WithRunningBackup(DateTimeOffset startedAt)
        {
            var world = new World();
            world.Build(seed: context =>
            {
                var backup = new Backup(
                    Guid.NewGuid(),
                    AccountId,
                    BackupDestination.DefaultDestinationId,
                    BackupKind.Manual,
                    startedAt);
                context.Backups.Add(backup);
                return backup.Id;
            });
            return world;
        }

        /// <summary>A world holding one COMPLETED backup started at <paramref name="startedAt"/>.</summary>
        /// <param name="startedAt">When the run began, before the boundary.</param>
        /// <returns>The world.</returns>
        public static World WithCompletedBackup(DateTimeOffset startedAt)
        {
            var world = new World();
            world.Build(seed: context =>
            {
                var backup = new Backup(
                    Guid.NewGuid(),
                    AccountId,
                    BackupDestination.DefaultDestinationId,
                    BackupKind.Manual,
                    startedAt);
                backup.Completed(4096, new string('a', 64), 2, startedAt.AddMinutes(1));
                context.Backups.Add(backup);
                return backup.Id;
            });
            return world;
        }

        /// <summary>Opens a second context over the same database, to read what a pass wrote.</summary>
        /// <returns>The reading context.</returns>
        public BackupsDbContext Read()
        {
            return BackupsTestContext.Create(_currentUser, _databaseName);
        }

        /// <summary>Seeds the database and builds the reconciler over it.</summary>
        /// <param name="seed">Adds the fixture's rows and answers the seeded backup's id.</param>
        private void Build(Func<BackupsDbContext, Guid>? seed)
        {
            var context = BackupsTestContext.CreateSeeded(_currentUser, _databaseName);
            if (seed is not null)
            {
                BackupId = seed(context);
                context.SaveChanges();
            }

            var scopes = new TestScopeFactory(
                context,
                Audit,
                _currentUser,
                new StubAccountDirectory(new AccountSnapshot(AccountId, Username, 1, 1, 1, 1, 1, 1, 1)));

            Reconciler = new StartupBackupReconciler(
                scopes.Scopes,
                Agent,
                new FakeClock(Now),
                NullLogger<StartupBackupReconciler>.Instance);
        }
    }
}
