using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Models;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Backups.Tests.Services;

/// <summary>
/// The rule that keeps "Verify now" from being a mail cannon: a finding is announced on the
/// transition into the failed state and at no other time (threat note R-H2).
/// </summary>
public sealed class BackupVerificationAnnouncerTests
{
    /// <summary>The system user name of the account the backup is of.</summary>
    private const string Username = "cust01";

    /// <summary>A finding that moved the backup into the failed state is journalled and mailed once.</summary>
    [Fact]
    public async Task A_finding_that_entered_the_failed_state_is_journalled_and_mailed_once()
    {
        var world = new World();

        await world.Announcer.AnnounceAsync(
            world.Backup,
            Username,
            new BackupVerificationOutcome(false, "BackupVerificationFailed", EnteredFailedState: true),
            CancellationToken.None);

        Assert.Single(world.Audit.Entries, entry => { return entry.Action == MonitoringAuditActions.AlertRaised; });
        Assert.Single(world.Bus.Published.OfType<SendMailRequested>());
    }

    /// <summary>A finding on a backup already recorded as failed is not announced.</summary>
    [Fact]
    public async Task A_finding_on_a_backup_already_recorded_as_failed_is_not_announced()
    {
        var world = new World();

        await world.Announcer.AnnounceAsync(
            world.Backup,
            Username,
            new BackupVerificationOutcome(false, "BackupVerificationFailed", EnteredFailedState: false),
            CancellationToken.None);

        Assert.Empty(world.Audit.Entries);
        Assert.Empty(world.Bus.Published);
    }

    /// <summary>A sound artifact and a verification that could not run announce nothing.</summary>
    [Fact]
    public async Task A_sound_artifact_and_a_verification_that_could_not_run_announce_nothing()
    {
        var world = new World();

        await world.Announcer.AnnounceAsync(
            world.Backup, Username, new BackupVerificationOutcome(true, null), CancellationToken.None);
        await world.Announcer.AnnounceAsync(
            world.Backup,
            Username,
            new BackupVerificationOutcome(
                false, null, Error.Of("AgentUnavailable", ErrorType.Unavailable)),
            CancellationToken.None);

        Assert.Empty(world.Audit.Entries);
        Assert.Empty(world.Bus.Published);
    }

    /// <summary>The doubles and the announcer over them.</summary>
    private sealed class World
    {
        /// <summary>The completed backup the outcomes are about.</summary>
        public Backup Backup { get; } = BackupsTestContext.CompletedRow(Guid.NewGuid());

        /// <summary>The audit double, holding what was journalled.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>The bus double, holding the mail that was requested.</summary>
        public RecordingMessageBus Bus { get; } = new();

        /// <summary>The announcer under test.</summary>
        public BackupVerificationAnnouncer Announcer { get; }

        /// <summary>Assembles the announcer over the real mailer and the doubles.</summary>
        public World()
        {
            Announcer = BackupRunnerFactory.Announcer(
                new BackupAuditJournal(Audit, FakeCurrentUser.Admin()), Bus);
        }
    }
}
