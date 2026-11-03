using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Commands.VerifyBackup;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Commands.VerifyBackup;

/// <summary>
/// Verifying one backup on request: what a sound artifact records, what a damaged one records, what
/// a verification that could not run must NOT record, and what another account's backup means.
/// </summary>
public sealed class VerifyBackupCommandHandlerTests
{
    /// <summary>The system user name the directory reports for the owning account.</summary>
    private const string Username = "cust01";

    /// <summary>A sound artifact is recorded as verified, dated, and journalled as a success.</summary>
    /// <remarks>
    /// The accepting case, and the inverse control for every refusal below: a handler mutated to
    /// refuse everything passes each of them and fails this one.
    /// </remarks>
    [Fact]
    public async Task A_sound_artifact_is_recorded_as_verified_and_journalled()
    {
        var accountId = Guid.NewGuid();
        var row = BackupsTestContext.CompletedRow(accountId);
        var world = await World.SeededAsync(accountId, row, accountId);

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BackupVerificationState.Verified, result.Value!.VerificationState);
        Assert.Equal("Verified", result.Value.VerificationStateName);
        Assert.Equal(World.Now, result.Value.VerifiedAt);
        Assert.Null(result.Value.VerificationFailureCode);
        Assert.Null(result.Value.VerificationFailureDisplayName);

        var asked = Assert.Single(world.Agent.Verifications);
        Assert.Equal(Username, asked.AccountUsername);
        Assert.Equal(row.Id.ToString(), asked.BackupId);
        Assert.IsType<AgentLocalBackupDestination>(Assert.Single(world.Agent.Destinations));

        var stored = await world.StoredAsync();
        Assert.Equal(BackupVerificationState.Verified, stored.VerificationState);
        Assert.Equal(World.Now, stored.VerifiedAt);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(BackupAuditActions.BackupVerified, entry.Action);
        Assert.Equal(Username, entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A corrupt artifact is an answer: the backup is returned, failed, with a code and its words.</summary>
    /// <remarks>
    /// The closing criterion of the issue for a corrupted artifact. The run's own status is asserted
    /// untouched, because a failed verification that rewrote it would turn a transient read failure
    /// into a backup retention treats as disposable.
    /// </remarks>
    [Fact]
    public async Task A_corrupt_artifact_is_recorded_as_failed_without_touching_the_run_status()
    {
        var accountId = Guid.NewGuid();
        var row = BackupsTestContext.CompletedRow(accountId);
        var world = await World.SeededAsync(
            accountId, row, accountId, Result<AgentBackupVerification>.Fail(
                Error.Of("AgentValidationFailed", ErrorType.Validation)));

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BackupVerificationState.Failed, result.Value!.VerificationState);
        Assert.Equal("BackupVerificationFailed", result.Value.VerificationFailureCode);
        Assert.False(string.IsNullOrEmpty(result.Value.VerificationFailureDisplayName));
        Assert.NotEqual("BackupVerificationFailed", result.Value.VerificationFailureDisplayName);
        Assert.Equal(BackupStatus.Completed, result.Value.Status);
        Assert.Null(result.Value.FailureCode);

        var stored = await world.StoredAsync();
        Assert.Equal(BackupVerificationState.Failed, stored.VerificationState);
        Assert.Equal("BackupVerificationFailed", stored.VerificationFailureCode);
        Assert.Equal(BackupStatus.Completed, stored.Status);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(BackupAuditActions.BackupVerificationFailed, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>An artifact the agent cannot find is a finding with its own code.</summary>
    [Fact]
    public async Task An_artifact_the_agent_cannot_find_is_recorded_as_failed_with_its_own_code()
    {
        var accountId = Guid.NewGuid();
        var row = BackupsTestContext.CompletedRow(accountId);
        var world = await World.SeededAsync(
            accountId, row, accountId, Result<AgentBackupVerification>.Fail(
                Error.Of("AgentNotFound", ErrorType.NotFound)));

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BackupVerificationState.Failed, result.Value!.VerificationState);
        Assert.Equal("BackupVerificationArtifactMissing", result.Value.VerificationFailureCode);
    }

    /// <summary>A verification that could not run is returned as an error and records nothing.</summary>
    /// <param name="agentCode">The code the agent client reported.</param>
    /// <param name="type">The kind the agent client reported it with.</param>
    /// <remarks>
    /// A busy lock, an unreachable or saturated agent, a destination the agent will not read and an
    /// input the panel itself got wrong each say nothing about the artifact. Marking the backup
    /// failed for any of them would mail "your backup is corrupt" because the server was busy.
    /// </remarks>
    [Theory]
    [InlineData("AgentUnavailable", ErrorType.Unavailable)]
    [InlineData("AgentSaturated", ErrorType.Unavailable)]
    [InlineData("AgentNotImplemented", ErrorType.Unavailable)]
    [InlineData("AgentSystemFailure", ErrorType.Failure)]
    [InlineData("AgentInvalidInput", ErrorType.Validation)]
    public async Task A_verification_that_could_not_run_records_nothing_and_is_not_a_finding(
        string agentCode,
        ErrorType type)
    {
        var accountId = Guid.NewGuid();
        var row = BackupsTestContext.CompletedRow(accountId);
        var world = await World.SeededAsync(
            accountId, row, accountId, Result<AgentBackupVerification>.Fail(Error.Of(agentCode, type)));

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(agentCode, result.Error!.Code);
        Assert.Equal(type, result.Error.Type);

        var stored = await world.StoredAsync();
        Assert.Equal(BackupVerificationState.NotVerified, stored.VerificationState);
        Assert.Null(stored.VerifiedAt);
        Assert.Null(stored.VerificationFailureCode);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(BackupAuditActions.BackupVerified, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>The agent's busy refusal is named as the account's operation still running.</summary>
    [Fact]
    public async Task A_busy_account_is_named_as_its_operation_still_running_and_records_nothing()
    {
        var accountId = Guid.NewGuid();
        var row = BackupsTestContext.CompletedRow(accountId);
        var world = await World.SeededAsync(
            accountId, row, accountId, Result<AgentBackupVerification>.Fail(
                Error.Of("AgentAlreadyExists", ErrorType.Conflict)));

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountBackupOperationRunning", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(BackupVerificationState.NotVerified, (await world.StoredAsync()).VerificationState);
    }

    /// <summary>A later verification replaces an earlier one: the last finding wins.</summary>
    [Fact]
    public async Task A_later_sound_verification_replaces_an_earlier_failed_one()
    {
        var accountId = Guid.NewGuid();
        var row = BackupsTestContext.CompletedRow(accountId);
        var world = await World.SeededAsync(
            accountId, row, accountId, Result<AgentBackupVerification>.Fail(
                Error.Of("AgentValidationFailed", ErrorType.Validation)));

        await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);
        world.Agent.VerifyResult = Result<AgentBackupVerification>.Ok(new AgentBackupVerification(3, 1, 4096));

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(BackupVerificationState.Verified, result.Value!.VerificationState);
        Assert.Null(result.Value.VerificationFailureCode);
        Assert.Equal(2, world.Audit.Entries.Count);
    }

    /// <summary>A backup that has no artifact to read is refused as a conflict and the agent is not asked.</summary>
    /// <param name="failed">Whether the row is a failed run rather than a running one.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_backup_without_a_finished_artifact_is_refused_and_the_agent_is_not_asked(bool failed)
    {
        var accountId = Guid.NewGuid();
        var row = BackupsTestContext.RunningRow(accountId);
        if (failed)
        {
            row.Failed("AgentSystemFailure", BackupsTestContext.SeedInstant);
        }

        var world = await World.SeededAsync(accountId, row, accountId);

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BackupNotVerifiable", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(world.Agent.Verifications);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>Another accounts backup is not found rather than forbidden, and the agent is never asked.</summary>
    /// <remarks>
    /// The IDOR case (rules/testing.md, Definition of Done 3). The assertion on the agent is what
    /// makes it more than a status check: a handler that answered 404 after making root read another
    /// customer's artifact would still be a read oracle.
    /// </remarks>
    [Fact]
    public async Task Another_accounts_backup_is_not_found_and_nothing_reaches_the_agent()
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
        Assert.Empty(world.Agent.Verifications);

        // The vacuity guard, on the axis that goes blind: the row must still BE there and untouched,
        // or the refusal above would be indistinguishable from a seed that never happened.
        var stored = await world.StoredAsync();
        Assert.Equal(BackupVerificationState.NotVerified, stored.VerificationState);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(BackupAuditActions.BackupVerified, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A backup whose account is gone is still verifiable, by its stamped name.</summary>
    [Fact]
    public async Task A_backup_whose_account_is_gone_is_verified_by_its_stamped_name()
    {
        var goneAccountId = Guid.NewGuid();
        var row = BackupsTestContext.CompletedRow(goneAccountId, kind: BackupKind.PreDeletion);
        row.Orphan("departed");

        var world = await World.SeededAsync(goneAccountId, row, Guid.NewGuid());

        var result = await world.Handler.HandleAsync(Command(row.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("departed", Assert.Single(world.Agent.Verifications).AccountUsername);
    }

    /// <summary>Builds a command naming <paramref name="backupId"/>.</summary>
    /// <param name="backupId">The backup to verify.</param>
    /// <returns>The command.</returns>
    private static VerifyBackupCommand Command(Guid backupId)
    {
        return new VerifyBackupCommand(backupId, "203.0.113.7", "tests");
    }

    /// <summary>The doubles, the database and the handler, assembled once per test.</summary>
    private sealed class World
    {
        /// <summary>The instant the clock double reports, so the recorded date is decidable.</summary>
        public static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 30, 0, TimeSpan.Zero);

        /// <summary>The in-memory database the handler and the reader share.</summary>
        private readonly string _database;

        /// <summary>The principal both contexts are bound to.</summary>
        private readonly FakeCurrentUser _currentUser;

        /// <summary>The handler under test.</summary>
        public VerifyBackupCommandHandler Handler { get; }

        /// <summary>The agent double, holding what it was asked to verify.</summary>
        public StubAgentBackupClient Agent { get; }

        /// <summary>The audit double, holding what was journalled.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>Assembles the handler over doubles.</summary>
        /// <param name="callerAccountId">The account the caller owns.</param>
        /// <param name="knownAccountId">The account the directory can answer for.</param>
        /// <param name="verifyResult">The answer the agent gives a verification; success when null.</param>
        private World(Guid callerAccountId, Guid knownAccountId, Result<AgentBackupVerification>? verifyResult)
        {
            _database = Guid.NewGuid().ToString();
            _currentUser = FakeCurrentUser.Customer(callerAccountId);

            Agent = new StubAgentBackupClient { VerifyResult = verifyResult };
            Audit = new RecordingAuditWriter();

            var dbContext = BackupsTestContext.CreateSeeded(_currentUser, _database);
            var journal = new BackupAuditJournal(Audit, _currentUser);

            Handler = new VerifyBackupCommandHandler(
                dbContext,
                new StubAccountDirectory(
                    new AccountSnapshot(knownAccountId, Username, 1, 1, 1, 1, 1, 1024, AccountState.Active)),
                new BackupVerifier(
                    dbContext, Agent, new BackupDestinationResolver(dbContext), journal, new FakeClock(Now)),
                journal,
                BackupsTestContext.FailureNames(),
                BackupsTestContext.BackupNames());
        }

        /// <summary>Assembles a world and seeds one row into its database.</summary>
        /// <param name="callerAccountId">The account the caller owns.</param>
        /// <param name="row">The row to seed.</param>
        /// <param name="knownAccountId">The account the directory can answer for.</param>
        /// <param name="verifyResult">The answer the agent gives a verification; success when omitted.</param>
        /// <returns>The assembled, seeded world.</returns>
        public static async Task<World> SeededAsync(
            Guid callerAccountId,
            Backup row,
            Guid knownAccountId,
            Result<AgentBackupVerification>? verifyResult = null)
        {
            var world = new World(callerAccountId, knownAccountId, verifyResult);

            // Seeded as an administrator so the seed itself never depends on the filter under test.
            await using var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), world._database);
            seeding.Backups.Add(row);
            await seeding.SaveChangesAsync();

            return world;
        }

        /// <summary>Reads the one stored row back through a second context, bypassing the tenant filter.</summary>
        /// <returns>The row as it now stands in the database.</returns>
        public async Task<Backup> StoredAsync()
        {
            await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), _database);

            return await reading.Backups.SingleAsync();
        }
    }
}
