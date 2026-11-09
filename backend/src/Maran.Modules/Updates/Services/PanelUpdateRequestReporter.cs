using Maran.Modules.Updates.Domain.Enums;
using Maran.Modules.Updates.Domain.ValueObjects;
using Maran.Modules.Updates.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Updates.Services;

/// <summary>
/// Moves the task of a requested update along, and closes it, from what the root update reports
/// (issue #382). Called by the <c>update-request-report</c> console verb, which the root <c>maran</c>
/// command runs as the service account.
/// </summary>
/// <remarks>
/// <para>
/// <b>The task id comes from the ledger, never from the report.</b> The report holds a word and two
/// release names; it cannot name a task. The ledger is the panel's own file naming the one open request,
/// so a report can only advance or close that one task, and a report with no ledger (a run nobody
/// requested from the panel) is still journalled and touches no task.
/// </para>
/// <para>
/// <b>What a task says.</b> The log lines are operator-facing English and name the two releases and the
/// fixed directory the pre-update dump is kept in, so an administrator reading the task finds where the
/// database could be restored from. A failure carries only a code from <see cref="ErrorMessages"/>; the
/// sentence an administrator reads is resolved from it in their language when the task is read.
/// </para>
/// </remarks>
public sealed class PanelUpdateRequestReporter
{
    /// <summary>How far along the task is once the root update has decided on a release.</summary>
    private const int InstallingPercent = 20;

    /// <summary>Where the root update keeps the pre-update database dump; a fixed directory, never a per-run path.</summary>
    private const string DumpDirectory = "/var/backups/maran-update";

    /// <summary>Records the update as a panel task.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>Names the task an open request opened.</summary>
    private readonly PanelUpdateTaskLedger _ledger;

    /// <summary>The module's audit journal.</summary>
    private readonly UpdatesAuditJournal _journal;

    /// <summary>Creates the reporter.</summary>
    /// <param name="tasks">Records the update as a panel task.</param>
    /// <param name="ledger">Names the task an open request opened.</param>
    /// <param name="journal">The module's audit journal.</param>
    public PanelUpdateRequestReporter(ITaskRecorder tasks, PanelUpdateTaskLedger ledger, UpdatesAuditJournal journal)
    {
        _tasks = tasks;
        _ledger = ledger;
        _journal = journal;
    }

    /// <summary>Applies one report.</summary>
    /// <param name="report">The word and release names root reported.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>Resolves once the task has been advanced or closed and the outcome journalled.</returns>
    public async Task ReportAsync(UpdateRequestReport report, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);

        var taskId = _ledger.TryRead();
        var to = report.To ?? report.From;

        if (report.Word == PanelUpdateRunWord.Installing)
        {
            if (taskId is { } open)
            {
                await _tasks.ReportAsync(open, InstallingPercent, $"Installing {to} over {report.From}.", cancellationToken);
            }

            return;
        }

        if (taskId is { } task)
        {
            await CloseAsync(task, report, to, cancellationToken);
        }

        _ledger.Remove();

        var (action, succeeded) = Outcome(report.Word);
        var subject = report.To is null ? $"from={report.From}" : $"from={report.From};to={report.To}";
        await _journal.RecordSystemAsync(action, subject, succeeded, cancellationToken);
    }

    /// <summary>Closes the task as the word says.</summary>
    /// <param name="taskId">The task the ledger names.</param>
    /// <param name="report">The report.</param>
    /// <param name="to">The release installed or tried; the running one when the report names none.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>Resolves once the task is closed.</returns>
    private async Task CloseAsync(Guid taskId, UpdateRequestReport report, string to, CancellationToken cancellationToken)
    {
        switch (report.Word)
        {
            case PanelUpdateRunWord.Applied:
                await _tasks.ReportAsync(
                    taskId,
                    InstallingPercent,
                    $"Updated {report.From} to {to}; the health check passed. The pre-update database dump is kept under {DumpDirectory}.",
                    cancellationToken);
                await _tasks.CompleteAsync(taskId, cancellationToken);
                break;
            case PanelUpdateRunWord.Unchanged:
                await _tasks.ReportAsync(taskId, InstallingPercent, $"Already on {report.From}; nothing to install.", cancellationToken);
                await _tasks.CompleteAsync(taskId, cancellationToken);
                break;
            case PanelUpdateRunWord.Refused:
                await _tasks.FailAsync(taskId, nameof(ErrorMessages.PanelUpdateRefused), cancellationToken);
                break;
            case PanelUpdateRunWord.RolledBack:
                await _tasks.ReportAsync(
                    taskId,
                    InstallingPercent,
                    $"The update to {to} failed its health check and was rolled back to {report.From}. The database was NOT restored. The pre-update dump is in {DumpDirectory}.",
                    cancellationToken);
                await _tasks.FailAsync(taskId, nameof(ErrorMessages.PanelUpdateRolledBack), cancellationToken);
                break;
            case PanelUpdateRunWord.RollbackFailed:
                await _tasks.ReportAsync(
                    taskId,
                    InstallingPercent,
                    $"The update to {to} failed and the rollback to {report.From} did not become healthy either. The pre-update dump is in {DumpDirectory}.",
                    cancellationToken);
                await _tasks.FailAsync(taskId, nameof(ErrorMessages.PanelUpdateRollbackFailed), cancellationToken);
                break;
            case PanelUpdateRunWord.Interrupted:
                await _tasks.ReportAsync(
                    taskId,
                    InstallingPercent,
                    $"The update to {to} was interrupted after the switch from {report.From}. The pre-update dump is in {DumpDirectory}.",
                    cancellationToken);
                await _tasks.FailAsync(taskId, nameof(ErrorMessages.PanelUpdateInterrupted), cancellationToken);
                break;
            default:
                break;
        }
    }

    /// <summary>The audit action a terminal word is journalled under, and whether it counts as having taken effect.</summary>
    /// <param name="word">A terminal word.</param>
    /// <returns>The action and whether the outcome is a success.</returns>
    private static (string Action, bool Succeeded) Outcome(PanelUpdateRunWord word)
    {
        return word switch
        {
            PanelUpdateRunWord.Applied => (UpdatesAuditActions.PanelUpdateRunApplied, true),
            PanelUpdateRunWord.Unchanged => (UpdatesAuditActions.PanelUpdateRunUnchanged, true),
            PanelUpdateRunWord.Refused => (UpdatesAuditActions.PanelUpdateRunRefused, false),
            PanelUpdateRunWord.RolledBack => (UpdatesAuditActions.PanelUpdateRunRolledBack, false),
            PanelUpdateRunWord.RollbackFailed => (UpdatesAuditActions.PanelUpdateRunRollbackFailed, false),
            PanelUpdateRunWord.Interrupted => (UpdatesAuditActions.PanelUpdateRunInterrupted, false),
            _ => throw new ArgumentOutOfRangeException(nameof(word), word, "Only a terminal word has an outcome."),
        };
    }
}
