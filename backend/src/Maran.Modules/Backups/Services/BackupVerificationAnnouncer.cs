using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Models;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// Tells the administrator about a backup that has just been found damaged, once, however many times
/// it is looked at afterwards (issue #134).
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule, in one place: mail on the transition into the failed state, never on a finding about
/// a backup already recorded as failed.</b> A finding made by a run's own verification and a finding
/// made by someone pressing "Verify now" are the same fact and go through the same rule. Without it
/// the button is a mail cannon: any login that can reach the endpoint could send the administrator
/// one message per press (threat note R-H2). With it, the first discovery is announced, the repeats
/// are silent, and a backup that is later found sound and then damaged again is a new event and is
/// announced again. The rate limit on the endpoint is a second, separate bound on the I/O each press
/// costs; it is not what keeps the mailbox quiet.
/// </para>
/// <para>
/// <b>A different code on an already-failed backup is not announced either.</b> The first mail named
/// the first finding, and the backup list shows the latest code; a second mail for the same
/// unrestorable-looking backup would be the per-press behaviour again with the wording changed.
/// </para>
/// <para>
/// <b>It never throws on the mail's account.</b> The finding is already recorded and journalled before
/// this runs; a mail that cannot be requested is logged and the caller carries on, because turning a
/// recorded finding into an error for the person who found it, or into a failed run for the run that
/// wrote the archive, would report a fault in the alert path as a fault in the backup. The cost is
/// that nothing re-sends it, since the backup is now already failed — which is why the failure is
/// logged at warning rather than swallowed.
/// </para>
/// </remarks>
public sealed class BackupVerificationAnnouncer
{
    /// <summary>Pre-compiled log delegate for an alert that could not be requested.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogAnnouncementFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(1, nameof(BackupVerificationAnnouncer)),
            "The alert for the failed verification of backup {BackupId} could not be raised; the finding "
            + "is recorded on the backup and is not re-sent");

    /// <summary>Raises the alert the administrator reads.</summary>
    private readonly BackupAlertMailer _alerts;

    /// <summary>Where an alert that could not be raised is reported.</summary>
    private readonly ILogger<BackupVerificationAnnouncer> _logger;

    /// <summary>Creates the announcer.</summary>
    /// <param name="alerts">Raises the alert the administrator reads.</param>
    /// <param name="logger">Where an alert that could not be raised is reported.</param>
    public BackupVerificationAnnouncer(BackupAlertMailer alerts, ILogger<BackupVerificationAnnouncer> logger)
    {
        _alerts = alerts;
        _logger = logger;
    }

    /// <summary>Announces a finding if, and only if, it is news.</summary>
    /// <param name="backup">The backup the verification was of.</param>
    /// <param name="accountUsername">The system user name of the account the backup is of.</param>
    /// <param name="outcome">What the verification concluded.</param>
    /// <param name="cancellationToken">Cancels the alert's journal writes.</param>
    /// <returns>A task that completes once the alert is raised, or it is decided there is none to raise.</returns>
    public async Task AnnounceAsync(
        Backup backup,
        string accountUsername,
        BackupVerificationOutcome outcome,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(outcome);

        if (!outcome.EnteredFailedState || outcome.FailureCode is null)
        {
            return;
        }

        try
        {
            await _alerts.VerificationFailedAsync(
                backup.Id,
                accountUsername,
                backup.FinishedAt ?? backup.StartedAt,
                outcome.FailureCode,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogAnnouncementFailed(_logger, backup.Id, exception);
        }
    }
}
