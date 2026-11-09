using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Alerts;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Backups.Tests.TestSupport;

/// <summary>
/// Assembles a <see cref="BackupRunner"/> over the module's real verifier and alert mailer and the
/// test doubles, so every test that drives a run builds it the same way.
/// </summary>
/// <remarks>
/// The runner now verifies a completed run's artifact and mails a failed verification, which means
/// it holds a verifier, a mailer and the journal both write through. Three test worlds build one;
/// assembling it here keeps them from drifting apart, and keeps the verifier and mailer REAL, so a
/// test observes the audit entries and mails they actually produce.
/// </remarks>
public static class BackupRunnerFactory
{
    /// <summary>Builds a runner over the given doubles.</summary>
    /// <param name="agent">The agent double that makes and verifies archives.</param>
    /// <param name="tasks">The task journal double.</param>
    /// <param name="dbContext">The context the verification state is saved through.</param>
    /// <param name="currentUser">The principal the journals are bound to.</param>
    /// <param name="audit">The audit double, holding what was journalled.</param>
    /// <param name="clock">The clock the verification is stamped with.</param>
    /// <param name="bus">The bus double the alert mail is published on; a fresh one when omitted.</param>
    /// <param name="recipients">Where alerts go; an address is configured when omitted.</param>
    /// <returns>The runner.</returns>
    public static BackupRunner Create(
        StubAgentBackupClient agent,
        RecordingTaskRecorder tasks,
        BackupsDbContext dbContext,
        ICurrentUser currentUser,
        RecordingAuditWriter audit,
        IClock clock,
        RecordingMessageBus? bus = null,
        StubAlertRecipientDirectory? recipients = null)
    {
        var journal = new BackupAuditJournal(audit, currentUser);

        return new BackupRunner(
            agent,
            tasks,
            new BackupVerifier(dbContext, agent, new BackupDestinationResolver(dbContext), journal, clock),
            Announcer(journal, bus ?? new RecordingMessageBus(), recipients),
            NullLogger<BackupRunner>.Instance);
    }

    /// <summary>Builds the announcer over the real alert mailer and the doubles.</summary>
    /// <param name="journal">The journal both the verification and the alert write through.</param>
    /// <param name="bus">The bus double the alert mail is published on.</param>
    /// <param name="recipients">Where alerts go; an address is configured when omitted.</param>
    /// <returns>The announcer.</returns>
    public static BackupVerificationAnnouncer Announcer(
        BackupAuditJournal journal,
        RecordingMessageBus bus,
        StubAlertRecipientDirectory? recipients = null)
    {
        return new BackupVerificationAnnouncer(
            new BackupAlertMailer(
                new OperatorAlertDispatcher(
                    recipients ?? new StubAlertRecipientDirectory("ops@example.test"),
                    bus,
                    new FakeClock(DateTimeOffset.UnixEpoch)),
                journal,
                BackupsTestContext.AlertText(),
                BackupsTestContext.FailureNames()),
            NullLogger<BackupVerificationAnnouncer>.Instance);
    }
}
