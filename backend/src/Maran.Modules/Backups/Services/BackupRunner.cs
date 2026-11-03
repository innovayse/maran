using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Models;
using Maran.Modules.Backups.Resources;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// Drives one create run: asks the agent for the backup, reports its progress onto the panel task,
/// and reduces the whole stream to the single outcome the row and the audit entry are written from.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the stream is consumed here and not in the handler.</b> Reducing a stream to an outcome
/// is the one piece of this operation that has a wrong answer which looks like a right one — the
/// terminal event may be a success, a named failure, or one of three endings that carry no code at
/// all — and the handler's job is to decide what to WRITE, not to work out what happened. Keeping
/// them apart is what lets the "a stream that never reported an outcome is not a success" rule be
/// tested on its own.
/// </para>
/// <para>
/// <b>Exactly one terminal event ends the loop, and the loop ending is itself terminal.</b> The
/// client contract promises a terminal event; if the sequence nonetheless runs dry, this returns a
/// truncated failure rather than whatever the last progress event happened to say. A run that
/// reported 99% and then stopped talking has produced nothing.
/// </para>
/// <para>
/// <b>Progress is reported onto the task, never onto the row.</b> The row records what a run
/// produced; the task records how far it has got. Writing progress into the row would mean a row
/// that changes shape while it is being read, and a percentage in a table whose other columns
/// describe a finished artifact.
/// </para>
/// <para>
/// <b>Verification follows a completed run and is its own fact (issue #134).</b>
/// <see cref="VerifyCompletedAsync"/> has <see cref="BackupVerifier"/> read the finished artifact back
/// and record the answer on the row beside the run's status, never in place of it: the run produced
/// an archive, and a later finding about that archive must not turn it into a failed run that
/// retention treats as disposable. A verification that could not run — the agent busy, saturated or
/// unreachable, a bound reached on a slow host — records nothing and mails nothing, because it found
/// nothing about the artifact; only a typed finding marks the row failed and tells the administrator.
/// </para>
/// </remarks>
public sealed class BackupRunner
{
    /// <summary>Pre-compiled log delegate for a verification that could not be carried out.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogVerificationUnavailable =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(1, nameof(BackupRunner)),
            "The verification of backup {BackupId} could not be carried out; the row keeps its previous "
            + "verification state and the run's outcome is unchanged");

    /// <summary>The agent, which owns every byte of every archive.</summary>
    private readonly IAgentBackupClient _agent;

    /// <summary>The panel-wide task journal, so an operator can watch a run instead of waiting on it.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>Reads a finished artifact back, records and journals what it found.</summary>
    private readonly BackupVerifier _verifier;

    /// <summary>Tells the administrator about an artifact that failed verification.</summary>
    private readonly BackupAlertMailer _alerts;

    /// <summary>Where a verification that could not be carried out is reported.</summary>
    private readonly ILogger<BackupRunner> _logger;

    /// <summary>Creates the runner.</summary>
    /// <param name="agent">The agent client that makes the archive.</param>
    /// <param name="tasks">The panel-wide task journal.</param>
    /// <param name="verifier">Reads a finished artifact back, records and journals what it found.</param>
    /// <param name="alerts">Tells the administrator about an artifact that failed verification.</param>
    /// <param name="logger">Where a verification that could not be carried out is reported.</param>
    public BackupRunner(
        IAgentBackupClient agent,
        ITaskRecorder tasks,
        BackupVerifier verifier,
        BackupAlertMailer alerts,
        ILogger<BackupRunner> logger)
    {
        _agent = agent;
        _tasks = tasks;
        _verifier = verifier;
        _alerts = alerts;
        _logger = logger;
    }

    /// <summary>Runs one backup to its end and reports what it produced.</summary>
    /// <param name="accountUsername">System user name of the account being backed up.</param>
    /// <param name="backupId">The identifier the artifact is stored under; the row's own id.</param>
    /// <param name="destination">
    /// Where the artifact is written, already resolved from a recorded row by
    /// <c>BackupDestinationResolver</c> — which is also where a destination this build cannot act on
    /// was refused. The runner is handed the answer rather than working one out, so there is exactly
    /// one place a destination is chosen and one place that choice is stamped onto the row.
    /// </param>
    /// <param name="taskId">The panel task this run's progress is reported onto.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>
    /// The outcome, successful only when the agent stated a finished artifact AND named it: an
    /// artifact reported with no digest is refused here, because the digest is what a later restore
    /// verifies the bytes against and a completed row without one is a backup nothing can check.
    /// </returns>
    public async Task<BackupRunOutcome> RunAsync(
        string accountUsername,
        Guid backupId,
        AgentBackupDestination destination,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await foreach (var progress in _agent.CreateAsync(
            accountUsername, backupId.ToString(), destination, cancellationToken))
        {
            if (progress.Kind == BackupCreateEventKind.Progress)
            {
                await _tasks.ReportAsync(taskId, (int)progress.Percent, progress.Stage, cancellationToken);
                continue;
            }

            if (progress.Kind != BackupCreateEventKind.Created)
            {
                return Failure(BackupAgentErrorTranslator.ToFailureCode(progress));
            }

            // A finished artifact the agent could not name is not one this panel will offer a
            // restore of. Refused here rather than stored, because the alternative is a Completed
            // row whose expected digest is empty — and the agent refuses an empty expected digest,
            // so the customer would learn about it during the restore instead.
            if (string.IsNullOrEmpty(progress.Sha256))
            {
                return Failure(nameof(ErrorMessages.BackupInvalidOutcome));
            }

            return new CreatedBackupOutcome(
                (long)progress.SizeBytes, progress.Sha256, (int)progress.DatabaseCount);
        }

        // The sequence ended without a terminal event at all. Whether an artifact exists on the
        // destination is unknown, so this is a truncation and never a completion.
        return Failure(nameof(ErrorMessages.BackupTruncated));
    }

    /// <summary>Reads a just-completed backup's artifact back and records what that found.</summary>
    /// <param name="backup">
    /// The row the run completed. A row that is not <see cref="Domain.Enums.BackupStatus.Completed"/>
    /// has no artifact and must not be passed; the entity refuses to record against it in any case.
    /// </param>
    /// <param name="accountUsername">System user name of the account the backup is of.</param>
    /// <param name="cancellationToken">Cancels the verification.</param>
    /// <returns>A task that completes once the verification is recorded and a finding is mailed.</returns>
    /// <remarks>
    /// <para>
    /// <b>Never throws on the verification's account, and so never makes the run fail.</b> The
    /// verification is bounded by the agent's own call deadlines; whatever it raises that is not the
    /// caller's cancellation — the agent, the write of the finding, the mail — is logged and read as
    /// "could not verify now", which leaves the run's outcome as it was. Reading a transient fault as
    /// a failed artifact would tell the administrator a backup is corrupt because a socket dropped.
    /// </para>
    /// <para>
    /// <b>The mail follows the record.</b> The verifier saves the finding before it answers, so the
    /// administrator is never told of a finding the panel's own screen does not show.
    /// </para>
    /// </remarks>
    public async Task VerifyCompletedAsync(
        Backup backup,
        string accountUsername,
        CancellationToken cancellationToken)
    {
        try
        {
            await VerifyAndAnnounceAsync(backup, accountUsername, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogVerificationUnavailable(_logger, backup.Id, exception);
        }
    }

    /// <summary>Asks the verifier and mails a finding about the artifact.</summary>
    /// <param name="backup">The completed row.</param>
    /// <param name="accountUsername">System user name of the account the backup is of.</param>
    /// <param name="cancellationToken">Cancels the verification and the writes.</param>
    /// <returns>A task that completes once the answer is handled.</returns>
    private async Task VerifyAndAnnounceAsync(
        Backup backup,
        string accountUsername,
        CancellationToken cancellationToken)
    {
        var outcome = await _verifier.VerifyAsync(backup, accountUsername, cancellationToken);

        // The verifier has already recorded and journalled a sound artifact or a finding. Only a
        // finding is announced: a verification that was not performed found nothing about the
        // artifact, and mailing it would tell the administrator a backup is corrupt because the
        // server was busy.
        if (outcome.FailureCode is null)
        {
            return;
        }

        await _alerts.VerificationFailedAsync(
            backup.Id, accountUsername, backup.FinishedAt ?? backup.StartedAt, outcome.FailureCode, cancellationToken);
    }

    /// <summary>Builds the outcome of a run that produced no usable artifact.</summary>
    /// <param name="failureCode">The machine-stable code naming what went wrong.</param>
    /// <returns>A <see cref="FailedBackupOutcome"/> carrying that code and no artifact figures.</returns>
    private static FailedBackupOutcome Failure(string failureCode)
    {
        return new FailedBackupOutcome(failureCode);
    }
}
