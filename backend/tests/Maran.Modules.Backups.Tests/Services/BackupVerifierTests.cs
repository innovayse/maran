using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Backups.Tests.Services;

/// <summary>
/// The one path a verification takes for the endpoint and for an unattended run alike: what it
/// records, what it journals and under whose name, and the three outcomes it distinguishes.
/// </summary>
public sealed class BackupVerifierTests
{
    /// <summary>The system user name the artifact is stored under.</summary>
    private const string Username = "cust01";

    /// <summary>The instant the clock double reports.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 30, 0, TimeSpan.Zero);

    /// <summary>A sound artifact verified without a caller is recorded and journalled under the panel's own actor.</summary>
    [Fact]
    public async Task A_sound_artifact_verified_without_a_caller_is_journalled_under_the_system_actor()
    {
        var world = World.Create();
        var row = world.Seed(BackupsTestContext.CompletedRow(Guid.NewGuid()));

        var outcome = await world.Verifier.VerifyAsync(row, Username, CancellationToken.None);

        Assert.True(outcome.Verified);
        Assert.Null(outcome.FailureCode);
        Assert.Null(outcome.NotPerformed);
        Assert.Equal(BackupVerificationState.Verified, row.VerificationState);
        Assert.Equal(Now, row.VerifiedAt);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(BackupAuditActions.BackupVerified, entry.Action);
        Assert.Equal(Username, entry.Subject);
        Assert.Equal(SystemAuditEntry.NameFor(BackupAuditJournal.ModuleName), entry.ActorUsername);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A corrupt artifact is a finding: recorded on the backup with its code, outcome carries the same code.</summary>
    [Fact]
    public async Task A_corrupt_artifact_is_a_finding_recorded_with_its_code()
    {
        var world = World.Create(Result<AgentBackupVerification>.Fail(
            Error.Of("AgentValidationFailed", ErrorType.Validation)));
        var row = world.Seed(BackupsTestContext.CompletedRow(Guid.NewGuid()));

        var outcome = await world.Verifier.VerifyAsync(row, Username, CancellationToken.None);

        Assert.False(outcome.Verified);
        Assert.Equal("BackupVerificationFailed", outcome.FailureCode);
        Assert.Null(outcome.NotPerformed);
        Assert.Equal(BackupVerificationState.Failed, row.VerificationState);
        Assert.Equal("BackupVerificationFailed", row.VerificationFailureCode);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(BackupAuditActions.BackupVerificationFailed, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>An unreachable agent is not a finding: the outcome says it was not performed and nothing is recorded.</summary>
    [Fact]
    public async Task An_unreachable_agent_is_reported_as_not_performed_and_records_nothing()
    {
        var world = World.Create(Result<AgentBackupVerification>.Fail(
            Error.Of("AgentUnavailable", ErrorType.Unavailable)));
        var row = world.Seed(BackupsTestContext.CompletedRow(Guid.NewGuid()));

        var outcome = await world.Verifier.VerifyAsync(row, Username, CancellationToken.None);

        Assert.False(outcome.Verified);
        Assert.Null(outcome.FailureCode);
        Assert.Equal("AgentUnavailable", outcome.NotPerformed!.Code);
        Assert.Equal(BackupVerificationState.NotVerified, row.VerificationState);
        Assert.Null(row.VerifiedAt);
        Assert.Null(row.VerificationFailureCode);

        // Journalled as a verification that did not take effect, under the action a sound read uses:
        // the finding's own action is reserved for a finding.
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(BackupAuditActions.BackupVerified, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A panel with no destination to read from asks the agent nothing and records nothing.</summary>
    [Fact]
    public async Task A_panel_with_no_destination_asks_the_agent_nothing_and_records_nothing()
    {
        var world = World.Create(verifyResult: null, seededDestination: false);
        var row = world.Seed(BackupsTestContext.CompletedRow(Guid.NewGuid()));

        var outcome = await world.Verifier.VerifyAsync(row, Username, CancellationToken.None);

        Assert.False(outcome.Verified);
        Assert.Equal("BackupDestinationNotConfigured", outcome.NotPerformed!.Code);
        Assert.Empty(world.Agent.Verifications);
        Assert.Equal(BackupVerificationState.NotVerified, row.VerificationState);
    }

    /// <summary>A verification a caller asked for is journalled under that caller, with the address it came from.</summary>
    [Fact]
    public async Task A_verification_a_caller_asked_for_is_journalled_under_that_caller()
    {
        var world = World.Create();
        var row = world.Seed(BackupsTestContext.CompletedRow(Guid.NewGuid()));

        var outcome = await world.Verifier.VerifyAsync(row, Username, "203.0.113.7", "tests", CancellationToken.None);

        Assert.True(outcome.Verified);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(BackupAuditActions.BackupVerified, entry.Action);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.Equal("tests", entry.UserAgent);
        Assert.NotEqual(SystemAuditEntry.NameFor(BackupAuditJournal.ModuleName), entry.ActorUsername);
    }

    /// <summary>The verifier asks the agent for the account and the backup id it was handed, and for nothing else.</summary>
    [Fact]
    public async Task The_agent_is_asked_for_the_account_and_backup_it_was_handed()
    {
        var world = World.Create();
        var row = world.Seed(BackupsTestContext.CompletedRow(Guid.NewGuid()));

        await world.Verifier.VerifyAsync(row, Username, CancellationToken.None);

        var asked = Assert.Single(world.Agent.Verifications);
        Assert.Equal(Username, asked.AccountUsername);
        Assert.Equal(row.Id.ToString(), asked.BackupId);
    }

    /// <summary>The verifier, its doubles and the database it saves to, assembled once per test.</summary>
    private sealed class World
    {
        /// <summary>The context the verifier saves through, and the test seeds into.</summary>
        private readonly BackupsDbContext _dbContext;

        /// <summary>The verifier under test.</summary>
        public BackupVerifier Verifier { get; }

        /// <summary>The agent double, holding what it was asked to verify.</summary>
        public StubAgentBackupClient Agent { get; }

        /// <summary>The audit double, holding what was journalled.</summary>
        public RecordingAuditWriter Audit { get; }

        /// <summary>Assembles the verifier over doubles.</summary>
        /// <param name="verifyResult">The answer the agent gives a verification; success when null.</param>
        /// <param name="seededDestination">Whether the database holds the default destination.</param>
        private World(Result<AgentBackupVerification>? verifyResult, bool seededDestination)
        {
            var currentUser = FakeCurrentUser.Admin();

            Agent = new StubAgentBackupClient { VerifyResult = verifyResult };
            Audit = new RecordingAuditWriter();
            _dbContext = seededDestination
                ? BackupsTestContext.CreateSeeded(currentUser)
                : BackupsTestContext.Create(currentUser);

            Verifier = new BackupVerifier(
                _dbContext,
                Agent,
                new BackupDestinationResolver(_dbContext),
                new BackupAuditJournal(Audit, currentUser),
                new FakeClock(Now));
        }

        /// <summary>Assembles a world.</summary>
        /// <param name="verifyResult">The answer the agent gives a verification; success when omitted.</param>
        /// <param name="seededDestination">Whether the database holds the default destination.</param>
        /// <returns>The assembled world.</returns>
        public static World Create(Result<AgentBackupVerification>? verifyResult = null, bool seededDestination = true)
        {
            return new World(verifyResult, seededDestination);
        }

        /// <summary>Adds a row to the context the verifier saves through, so it is tracked there.</summary>
        /// <param name="row">The row to add.</param>
        /// <returns>The same row, now tracked.</returns>
        public Backup Seed(Backup row)
        {
            _dbContext.Backups.Add(row);
            _dbContext.SaveChanges();

            return row;
        }
    }
}
