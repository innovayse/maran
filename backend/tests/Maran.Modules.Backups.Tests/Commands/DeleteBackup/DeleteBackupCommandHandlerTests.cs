using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Commands.DeleteBackup;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Commands.DeleteBackup;

/// <summary>
/// Deleting one backup: the artifact first and the row second, what an already-gone artifact means,
/// what a running backup means, and what another account's backup means.
/// </summary>
public sealed class DeleteBackupCommandHandlerTests
{
    /// <summary>The system user name the directory reports for the owning account.</summary>
    private const string Username = "cust01";

    /// <summary>A completed backup is removed from the agent and from the panel.</summary>
    /// <remarks>
    /// The accepting case, and the inverse control for all three refusals below: a handler mutated
    /// to refuse everything — an inverted tenancy check, an inverted <c>MayBeDeleted</c>, an
    /// always-failing agent read — passes every refusal test and fails this one.
    /// </remarks>
    [Fact]
    public async Task A_completed_backup_is_removed_from_the_agent_and_from_the_panel()
    {
        var accountId = Guid.NewGuid();
        var row = BackupsTestContext.CompletedRow(accountId);
        var world = await World.SeededAsync(accountId, row, accountId);

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);

        var call = Assert.Single(world.Agent.Deletes);
        Assert.Equal(Username, call.AccountUsername);
        Assert.Equal(row.Id.ToString(), call.BackupId);

        var destination = Assert.Single(world.Agent.Destinations);
        Assert.Equal(AgentBackupDestinationKind.Local, destination.Kind);

        // Empty, not the configured root: the agent refuses a local destination that carries a path,
        // so an assertion that this held the root was certifying a call a real agent turns away.
        Assert.Equal(string.Empty, destination.Path);

        Assert.Empty(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.BackupDeleted, entry.Action);

        // The account, not the backup id: the subject is what an operator will search the journal
        // for once the archive is gone.
        Assert.Equal(Username, entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An artifact that is already gone is a success rather than an undeletable row.</summary>
    [Fact]
    public async Task An_artifact_that_is_already_gone_is_a_success()
    {
        var accountId = Guid.NewGuid();
        var row = BackupsTestContext.CompletedRow(accountId);
        var world = await World.SeededAsync(
            accountId,
            row,
            accountId,
            Result<bool>.Fail(Error.Of("BackupNotFound", ErrorType.NotFound)));

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>Any other agent refusal keeps the row, so nothing is orphaned on the destination.</summary>
    [Fact]
    public async Task Any_other_agent_refusal_keeps_the_row()
    {
        var accountId = Guid.NewGuid();
        var row = BackupsTestContext.CompletedRow(accountId);
        var world = await World.SeededAsync(
            accountId,
            row,
            accountId,
            Result<bool>.Fail(Error.Of("AgentUnavailable", ErrorType.Unavailable)));

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);

        // The row survives: deleting it here would leave a customer's archive on the destination
        // with nothing naming it.
        Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>The agent's busy refusal is named as the account's operation still running.</summary>
    /// <remarks>
    /// The agent's per-account lock answers a deletion issued while a backup or restore of that
    /// account is running with <c>BackupError::AlreadyRunning</c>, which reaches the panel as the
    /// wire's already-exists — the same code the agent uses for the idempotent outcome of a repeated
    /// CREATION, whose shared sentence tells the customer nothing was created again. A customer told
    /// that about a deletion believes the archive is gone and never retries, so the code is renamed
    /// here. It is separable because the agent produces already-exists from exactly one place, the
    /// creation path, which a deletion cannot reach.
    ///
    /// The row must survive, and it is asserted here rather than left to
    /// <see cref="Any_other_agent_refusal_keeps_the_row"/>: a rename that also took the wrong branch
    /// would delete the panel's only record of an archive that is still on the destination.
    /// </remarks>
    [Fact]
    public async Task An_agent_already_exists_is_named_as_the_accounts_operation_still_running()
    {
        var accountId = Guid.NewGuid();
        var row = BackupsTestContext.CompletedRow(accountId);
        var world = await World.SeededAsync(
            accountId,
            row,
            accountId,
            Result<bool>.Fail(Error.Of("AgentAlreadyExists", ErrorType.Conflict)));

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountBackupOperationRunning", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);

        Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>A running backup is refused as a conflict and its artifact is left alone.</summary>
    [Fact]
    public async Task A_running_backup_is_refused_as_a_conflict()
    {
        var accountId = Guid.NewGuid();
        var row = BackupsTestContext.RunningRow(accountId);
        var world = await World.SeededAsync(accountId, row, accountId);

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BackupStillRunning", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);

        Assert.Empty(world.Agent.Deletes);
        Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>Another accounts backup is not found rather than forbidden, and is not deleted.</summary>
    /// <remarks>
    /// The IDOR case for deletion (rules/testing.md, Definition of Done 3). The refusal is
    /// <see cref="ErrorType.NotFound"/> and never <see cref="ErrorType.Forbidden"/>, and the
    /// assertions on the agent and on the surviving row are what make this more than a status check:
    /// a handler that answered 404 after removing the archive would still have destroyed another
    /// customer's backup.
    /// </remarks>
    [Fact]
    public async Task Another_accounts_backup_is_not_found_and_is_not_deleted()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var theirRow = BackupsTestContext.CompletedRow(theirs);
        var world = await World.SeededAsync(mine, theirRow, theirs);

        var result = await world.Handler.HandleAsync(Command(theirRow.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BackupNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.NotEqual(ErrorType.Forbidden, result.Error.Type);

        Assert.Empty(world.Agent.Deletes);

        // The vacuity guard, on the axis that goes blind: the row must still BE there, or the
        // refusal above would be indistinguishable from a seed that never happened.
        Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.BackupDeleted, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>Builds a command naming <paramref name="backupId"/>.</summary>
    /// <param name="backupId">The backup to delete.</param>
    /// <returns>The command.</returns>
    private static DeleteBackupCommand Command(Guid backupId)
    {
        return new DeleteBackupCommand(backupId, "203.0.113.7", "tests");
    }

    /// <summary>One assembled handler, its seeded database and every double it was built from.</summary>
    /// <summary>A backup whose account has been deleted is still deletable, by its stamped name.</summary>
    /// <remarks>
    /// <para>
    /// The defect this closes was total and silent. A pre-deletion backup outlives its account by
    /// design, so the accounts directory answers "no such account" for it for ever — and while this
    /// handler read the name only from the directory, every such row answered <c>AccountNotFound</c>
    /// and could never be removed. The rows and their archives accumulated at one per deleted
    /// account with nothing in the product able to bound either, and retention is deliberately not
    /// what bounds them: an administrator releases them, which means an administrator has to be able
    /// to.
    /// </para>
    /// <para>
    /// The agent is asserted to have been aimed at the STAMPED name, not at whatever the directory
    /// would have said, because a delete aimed at the wrong directory is the failure this stamp's
    /// own overwrite rule exists against.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_backup_whose_account_is_gone_is_still_deletable_by_its_stamped_name()
    {
        var goneAccountId = Guid.NewGuid();
        var row = BackupsTestContext.CompletedRow(goneAccountId, kind: BackupKind.PreDeletion);
        row.Orphan("departed");

        // The directory knows a DIFFERENT account, which is what "this account no longer exists"
        // looks like from here.
        var world = await World.SeededAsync(goneAccountId, row, Guid.NewGuid());

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("departed", Assert.Single(world.Agent.Deletes).AccountUsername);
        Assert.Empty(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>The doubles, the database and the handler, assembled once per test.</summary>
    private sealed class World
    {
        /// <summary>The in-memory database the handler and the reader share.</summary>
        private readonly string _database;

        /// <summary>The principal both contexts are bound to.</summary>
        private readonly FakeCurrentUser _currentUser;

        /// <summary>The handler under test.</summary>
        public DeleteBackupCommandHandler Handler { get; }

        /// <summary>The agent double, holding what it was asked to delete.</summary>
        public StubAgentBackupClient Agent { get; }

        /// <summary>The audit double, holding what was journalled.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>Assembles the handler over doubles.</summary>
        /// <param name="callerAccountId">The account the caller owns.</param>
        /// <param name="knownAccountId">The account the directory can answer for.</param>
        /// <param name="deleteResult">The answer the agent gives a deletion.</param>
        private World(Guid callerAccountId, Guid knownAccountId, Result<bool> deleteResult)
        {
            _database = Guid.NewGuid().ToString();
            _currentUser = FakeCurrentUser.Customer(callerAccountId);

            Agent = new StubAgentBackupClient(createEvents: null, deleteResult);
            Audit = new RecordingAuditWriter();

            var dbContext = BackupsTestContext.CreateSeeded(_currentUser, _database);

            Handler = new DeleteBackupCommandHandler(
                dbContext,
                new StubAccountDirectory(
                    new AccountSnapshot(knownAccountId, Username, 1, 1, 1, 1, 1, 1024)),
                Agent,
                new BackupAuditJournal(Audit, _currentUser),
                new BackupDestinationResolver(dbContext));
        }

        /// <summary>Assembles a world and seeds one row into its database.</summary>
        /// <param name="callerAccountId">The account the caller owns.</param>
        /// <param name="row">The row to seed.</param>
        /// <param name="knownAccountId">The account the directory can answer for.</param>
        /// <param name="deleteResult">The answer the agent gives a deletion; success when omitted.</param>
        /// <returns>The assembled, seeded world.</returns>
        public static async Task<World> SeededAsync(
            Guid callerAccountId,
            Backup row,
            Guid knownAccountId,
            Result<bool>? deleteResult = null)
        {
            var world = new World(callerAccountId, knownAccountId, deleteResult ?? Result<bool>.Ok(true));

            // Seeded as an administrator so the seed itself never depends on the filter under test.
            await using var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), world._database);
            seeding.Backups.Add(row);
            await seeding.SaveChangesAsync();

            return world;
        }

        /// <summary>Opens a second context over the same database, to read what survived.</summary>
        /// <returns>A context bound to the same principal and database.</returns>
        public BackupsDbContext Read()
        {
            return BackupsTestContext.Create(_currentUser, _database);
        }
    }
}
