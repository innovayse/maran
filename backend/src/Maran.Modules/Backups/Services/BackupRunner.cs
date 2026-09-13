using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Models;
using Maran.Modules.Backups.Resources;
using Maran.Sdk.Interfaces;

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
/// </remarks>
public sealed class BackupRunner
{
    /// <summary>The agent, which owns every byte of every archive.</summary>
    private readonly IAgentBackupClient _agent;

    /// <summary>The panel-wide task journal, so an operator can watch a run instead of waiting on it.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>Creates the runner.</summary>
    /// <param name="agent">The agent client that makes the archive.</param>
    /// <param name="tasks">The panel-wide task journal.</param>
    public BackupRunner(IAgentBackupClient agent, ITaskRecorder tasks)
    {
        _agent = agent;
        _tasks = tasks;
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

            return new BackupRunOutcome(
                Succeeded: true,
                (long)progress.SizeBytes,
                progress.Sha256,
                (int)progress.DatabaseCount,
                string.Empty);
        }

        // The sequence ended without a terminal event at all. Whether an artifact exists on the
        // destination is unknown, so this is a truncation and never a completion.
        return Failure(nameof(ErrorMessages.BackupTruncated));
    }

    /// <summary>Builds the outcome of a run that produced no usable artifact.</summary>
    /// <param name="failureCode">The machine-stable code naming what went wrong.</param>
    /// <returns>An unsuccessful outcome carrying that code and no artifact figures.</returns>
    private static BackupRunOutcome Failure(string failureCode)
    {
        return new BackupRunOutcome(Succeeded: false, 0, string.Empty, 0, failureCode);
    }
}
