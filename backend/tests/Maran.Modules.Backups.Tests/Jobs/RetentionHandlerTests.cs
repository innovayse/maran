using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Jobs;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Backups.Tests.Jobs;

/// <summary>
/// What one retention pass removes, what it must never remove, and — the assertion that matters
/// most — what it does when it cannot reach the bytes it was going to delete.
/// </summary>
public sealed class RetentionHandlerTests
{
    /// <summary>The system user name the directory reports for the account.</summary>
    private const string Username = "cust01";

    /// <summary>The instant the oldest seeded backup was taken.</summary>
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Retention keeps exactly the retained number, newest first.</summary>
    [Fact]
    public async Task Retention_keeps_exactly_the_retained_number()
    {
        var accountId = Guid.NewGuid();
        var rows = Enumerable.Range(0, 5)
            .Select(day => { return Completed(accountId, Origin.AddDays(day)); })
            .ToList();

        var world = await World.SeededAsync(accountId, rows);

        var pruned = await world.Handler.HandleAsync(new RetentionRequested(accountId, 2), CancellationToken.None);

        Assert.Equal(3, pruned);

        var kept = await world.Read().Backups.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, kept.Count);
        Assert.Equal(
            new[] { rows[3].Id, rows[4].Id }.OrderBy(id => { return id; }),
            kept.Select(row => { return row.Id; }).OrderBy(id => { return id; }));
    }

    /// <summary>Nothing is pruned while the account has no more than the retained number.</summary>
    /// <remarks>The ordinary outcome on most nights, and the inverse control for every deletion below.</remarks>
    [Fact]
    public async Task Nothing_is_pruned_below_the_retained_number()
    {
        var accountId = Guid.NewGuid();
        var rows = new List<Backup> { Completed(accountId, Origin), Completed(accountId, Origin.AddDays(1)) };
        var world = await World.SeededAsync(accountId, rows);

        var pruned = await world.Handler.HandleAsync(new RetentionRequested(accountId, 7), CancellationToken.None);

        Assert.Equal(0, pruned);
        Assert.Empty(world.Agent.Deletes);
        Assert.Equal(2, await world.Read().Backups.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>Retention never removes a pre-restore or a pre-deletion backup.</summary>
    /// <remarks>
    /// R12. Both are safety copies taken immediately before an operation that destroys something,
    /// and a safety copy retention can eat is not a safety copy. They are not counted toward the
    /// retained number either — a retain of one over four safety copies and two ordinary ones must
    /// prune exactly one ordinary backup, which is what this asserts.
    /// </remarks>
    [Fact]
    public async Task Retention_never_removes_a_safety_copy()
    {
        var accountId = Guid.NewGuid();
        var preRestore = Completed(accountId, Origin, BackupKind.PreRestore);
        var preDeletion = Completed(accountId, Origin.AddDays(1), BackupKind.PreDeletion);
        var older = Completed(accountId, Origin.AddDays(2));
        var newer = Completed(accountId, Origin.AddDays(3));

        var world = await World.SeededAsync(accountId, [preRestore, preDeletion, older, newer]);

        var pruned = await world.Handler.HandleAsync(new RetentionRequested(accountId, 1), CancellationToken.None);

        Assert.Equal(1, pruned);

        var remaining = await world.Read().Backups.IgnoreQueryFilters()
            .Select(row => row.Id).ToListAsync();

        Assert.Contains(preRestore.Id, remaining);
        Assert.Contains(preDeletion.Id, remaining);
        Assert.Contains(newer.Id, remaining);
        Assert.DoesNotContain(older.Id, remaining);
    }

    /// <summary>A failed backup is neither counted nor pruned.</summary>
    /// <remarks>
    /// If failures counted toward the retained number, a bad week would silently expire every good
    /// copy the account still had — which is the opposite of what an operator means by "keep three".
    /// </remarks>
    [Fact]
    public async Task A_failed_backup_is_neither_counted_nor_pruned()
    {
        var accountId = Guid.NewGuid();
        var failed = new Backup(Guid.NewGuid(), accountId, null, BackupKind.Scheduled, Origin);
        failed.Failed("BackupTruncated", Origin.AddMinutes(1));

        var older = Completed(accountId, Origin.AddDays(1));
        var newer = Completed(accountId, Origin.AddDays(2));

        var world = await World.SeededAsync(accountId, [failed, older, newer]);

        var pruned = await world.Handler.HandleAsync(new RetentionRequested(accountId, 1), CancellationToken.None);

        Assert.Equal(1, pruned);

        var remaining = await world.Read().Backups.IgnoreQueryFilters()
            .Select(row => row.Id).ToListAsync();

        Assert.Contains(failed.Id, remaining);
        Assert.Contains(newer.Id, remaining);
    }

    /// <summary>The archive is deleted before the row, and the oldest goes first.</summary>
    [Fact]
    public async Task The_archive_goes_first_and_the_oldest_goes_first()
    {
        var accountId = Guid.NewGuid();
        var rows = Enumerable.Range(0, 4)
            .Select(day => { return Completed(accountId, Origin.AddDays(day)); })
            .ToList();

        var world = await World.SeededAsync(accountId, rows);

        await world.Handler.HandleAsync(new RetentionRequested(accountId, 1), CancellationToken.None);

        Assert.Equal(
            new[] { rows[0].Id.ToString(), rows[1].Id.ToString(), rows[2].Id.ToString() },
            world.Agent.Deletes.Select(call => { return call.BackupId; }));
        // Empty, not the configured root: the agent refuses a local destination that carries a path.
        Assert.All(world.Agent.Destinations, destination => { Assert.Equal(string.Empty, destination.Path); });
    }

    /// <summary>An archive the agent says is already gone still takes its row with it.</summary>
    [Fact]
    public async Task An_archive_that_is_already_gone_still_takes_its_row()
    {
        var accountId = Guid.NewGuid();
        var rows = new List<Backup> { Completed(accountId, Origin), Completed(accountId, Origin.AddDays(1)) };
        var world = await World.SeededAsync(
            accountId, rows, Result<bool>.Fail(Error.Of("BackupNotFound", ErrorType.NotFound)));

        var pruned = await world.Handler.HandleAsync(new RetentionRequested(accountId, 1), CancellationToken.None);

        Assert.Equal(1, pruned);
        Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>An agent refusal leaves the row alone and stops the pass.</summary>
    /// <remarks>
    /// <para>
    /// The assertion this whole handler is shaped around. Deleting the row over an archive that is
    /// still there produces a customer's files and the contents of their database sitting on the
    /// disk for ever, owned by nothing and invisible to every later pass — which is strictly worse
    /// than an account that stays over its retained count for a night.
    /// </para>
    /// <para>
    /// Stopping rather than continuing is the second half: the likeliest refusal is the agent's
    /// per-account lock, held by a RESTORE reading one of these very archives, and the rest of the
    /// pass would hit it identically.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_agent_refusal_leaves_the_row_alone_and_stops_the_pass()
    {
        var accountId = Guid.NewGuid();
        var rows = Enumerable.Range(0, 4)
            .Select(day => { return Completed(accountId, Origin.AddDays(day)); })
            .ToList();

        var world = await World.SeededAsync(
            accountId, rows, Result<bool>.Fail(Error.Of("BackupAlreadyRunning", ErrorType.Conflict)));

        var pruned = await world.Handler.HandleAsync(new RetentionRequested(accountId, 1), CancellationToken.None);

        Assert.Equal(0, pruned);
        Assert.Single(world.Agent.Deletes);
        Assert.Equal(4, await world.Read().Backups.IgnoreQueryFilters().CountAsync());

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.BackupRetentionPruned, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A backup on a destination the panel cannot address is left entirely alone.</summary>
    /// <remarks>
    /// The only destination this panel can act on is the operator's configured local root. A row
    /// naming any other one is skipped — neither its archive asked for nor its row removed — because
    /// a pass that deleted a row whose bytes it could not reach would report a tidy table over an
    /// archive of a customer's database that nobody can now find.
    /// </remarks>
    [Fact]
    public async Task A_backup_on_an_unreachable_destination_is_left_alone()
    {
        var accountId = Guid.NewGuid();
        var remote = new Backup(Guid.NewGuid(), accountId, Guid.NewGuid(), BackupKind.Scheduled, Origin);
        remote.Completed(1024, new string('a', 64), 1, Origin.AddMinutes(1));

        var local = Completed(accountId, Origin.AddDays(1));
        var newest = Completed(accountId, Origin.AddDays(2));

        var world = await World.SeededAsync(accountId, [remote, local, newest]);

        var pruned = await world.Handler.HandleAsync(new RetentionRequested(accountId, 1), CancellationToken.None);

        Assert.Equal(1, pruned);
        Assert.Equal(local.Id.ToString(), Assert.Single(world.Agent.Deletes).BackupId);

        var remaining = await world.Read().Backups.IgnoreQueryFilters()
            .Select(row => row.Id).ToListAsync();

        Assert.Contains(remote.Id, remaining);
        Assert.Contains(newest.Id, remaining);
    }

    /// <summary>Another account's backups are never touched by one account's pass.</summary>
    [Fact]
    public async Task Another_accounts_backups_are_never_touched()
    {
        var accountId = Guid.NewGuid();
        var stranger = Guid.NewGuid();

        var mine = Enumerable.Range(0, 3)
            .Select(day => { return Completed(accountId, Origin.AddDays(day)); })
            .ToList();
        var theirs = Enumerable.Range(0, 3)
            .Select(day => { return Completed(stranger, Origin.AddDays(day)); })
            .ToList();

        var world = await World.SeededAsync(accountId, [.. mine, .. theirs]);

        await world.Handler.HandleAsync(new RetentionRequested(accountId, 1), CancellationToken.None);

        var remaining = await world.Read().Backups.IgnoreQueryFilters()
            .Where(row => row.AccountId == stranger).CountAsync();

        Assert.Equal(3, remaining);
    }

    /// <summary>A pruning is journalled as the panel, not as an anonymous caller.</summary>
    [Fact]
    public async Task A_pruning_is_journalled_as_the_panel()
    {
        var accountId = Guid.NewGuid();
        var rows = new List<Backup> { Completed(accountId, Origin), Completed(accountId, Origin.AddDays(1)) };
        var world = await World.SeededAsync(accountId, rows);

        await world.Handler.HandleAsync(new RetentionRequested(accountId, 1), CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.BackupRetentionPruned, entry.Action);
        Assert.Equal(rows[0].Id.ToString(), entry.Subject);
        Assert.Null(entry.ActorUserId);
        Assert.Equal(SystemAuditEntry.NameFor(BackupAuditJournal.ModuleName), entry.ActorUsername);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Builds a completed backup row.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="startedAt">When the run began, which is what retention orders by.</param>
    /// <param name="kind">Why the backup was taken.</param>
    /// <returns>The row.</returns>
    private static Backup Completed(
        Guid accountId,
        DateTimeOffset startedAt,
        BackupKind kind = BackupKind.Scheduled)
    {
        var backup = new Backup(Guid.NewGuid(), accountId, destinationId: null, kind, startedAt);
        backup.Completed(1024, new string('a', 64), 1, startedAt.AddMinutes(1));

        return backup;
    }

    /// <summary>The doubles, the database and the handler, assembled once per test.</summary>
    private sealed class World
    {
        /// <summary>The in-memory database the handler and the reader share.</summary>
        private readonly string _database;

        /// <summary>The principal both contexts are bound to; an administrator sees every row.</summary>
        private readonly FakeCurrentUser _currentUser;

        /// <summary>The handler under test.</summary>
        public RetentionHandler Handler { get; }

        /// <summary>The agent double, holding what it was asked to delete.</summary>
        public StubAgentBackupClient Agent { get; }

        /// <summary>The audit double, holding what was journalled.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>Assembles the handler over doubles.</summary>
        /// <param name="accountId">The account the directory knows.</param>
        /// <param name="deleteResult">The answer the agent gives every delete.</param>
        private World(Guid accountId, Result<bool> deleteResult)
        {
            _database = Guid.NewGuid().ToString();
            _currentUser = FakeCurrentUser.Admin();

            Agent = new StubAgentBackupClient(deleteResult: deleteResult);
            Audit = new RecordingAuditWriter();

            var dbContext = BackupsTestContext.CreateSeeded(_currentUser, _database);

            Handler = new RetentionHandler(
                dbContext,
                new StubAccountDirectory(new AccountSnapshot(accountId, Username, 1, 1, 1, 1, 1, 1024)),
                Agent,
                new BackupAuditJournal(Audit, _currentUser),
                new BackupDestinationResolver(dbContext),
                NullLogger<RetentionHandler>.Instance);
        }

        /// <summary>Seeds the rows and assembles the world.</summary>
        /// <param name="accountId">The account the directory knows.</param>
        /// <param name="rows">The backup rows to seed.</param>
        /// <param name="deleteResult">The answer the agent gives every delete; success when omitted.</param>
        /// <returns>The assembled world.</returns>
        public static async Task<World> SeededAsync(
            Guid accountId,
            IReadOnlyList<Backup> rows,
            Result<bool>? deleteResult = null)
        {
            var world = new World(accountId, deleteResult ?? Result<bool>.Ok(true));

            await using var seed = BackupsTestContext.Create(world._currentUser, world._database);
            seed.Backups.AddRange(rows);
            await seed.SaveChangesAsync();

            return world;
        }

        /// <summary>Opens a context over the same database, to read what the pass wrote.</summary>
        /// <returns>A context bound to the same principal and database.</returns>
        public BackupsDbContext Read()
        {
            return BackupsTestContext.Create(_currentUser, _database);
        }
    }
}
