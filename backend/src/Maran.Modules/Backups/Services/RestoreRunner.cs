using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Mappers;
using Maran.Modules.Backups.Models;
using Maran.Modules.Backups.Resources;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Services;

/// <summary>
/// Drives one restore run: asks the agent to replace an account from an archive, reports its
/// progress onto the panel task, and reduces the whole stream to the single outcome the response,
/// the task and the audit entry are written from.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one decision in this module that a wrong answer looks right for.</b> The agent's
/// <c>RestoreOutcome</c> carries no success field on purpose — three numbers and no verdict — so
/// that the panel has to state the verdict itself and can be tested on it. That statement is made
/// HERE, once: <c>Whole</c> is <c>FilesRestored &amp;&amp; DatabasesRestored == DatabasesTotal</c>,
/// and everything downstream reads it rather than recomputing it. A restore that replaced the home
/// and eleven of twelve databases is a FAILURE, and the account is now a home from one point in time
/// over a database from another.
/// </para>
/// <para>
/// <b>An outcome-bearing arm with no outcome is refused, not read as zero.</b> The client's
/// <c>Restored</c> event may carry a null outcome — the arm is exactly-one on the wire but the
/// message inside it is not guaranteed present, which is the same partiality the C# client already
/// refuses a manifest-less readable backup over. Null is "the agent stated nothing", never "nothing
/// was restored", and a caller that rendered the two the same way would report a clean no-op over an
/// account that may have been half-replaced.
/// </para>
/// <para>
/// <b>The loop ending is itself terminal.</b> A stream that ran dry after reporting ninety percent
/// has said nothing about what it did; that is a truncation, and it is the worst state this contract
/// can report.
/// </para>
/// <para>
/// <b>Progress goes onto the task and nowhere else.</b> A restore writes no row of its own — the
/// <c>Backup</c> row it reads describes an artifact and is not changed by being restored from — so
/// the task is the whole of what an operator can watch.
/// </para>
/// </remarks>
public sealed class RestoreRunner
{
    /// <summary>The agent, which owns every byte of every archive and every database on the host.</summary>
    private readonly IAgentBackupClient _agent;

    /// <summary>The panel-wide task journal, so an operator can watch a restore instead of waiting on it.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>The module's settings, which name the destination the artifact is read from.</summary>
    /// <summary>Creates the runner.</summary>
    /// <param name="agent">The agent client that performs the restore.</param>
    /// <param name="tasks">The panel-wide task journal.</param>
    public RestoreRunner(IAgentBackupClient agent, ITaskRecorder tasks)
    {
        _agent = agent;
        _tasks = tasks;
    }

    /// <summary>Runs one restore to its end and reports what it actually did.</summary>
    /// <param name="accountUsername">System user name of the account being replaced.</param>
    /// <param name="backupId">The artifact to restore from; the <c>Backup</c> row's own id.</param>
    /// <param name="expectedSha256">
    /// The digest the panel recorded when the artifact was made. Handed to the agent as what it must
    /// verify the bytes against — the panel's own copy, not the sidecar beside the artifact, because
    /// a digest read from next to the bytes it describes proves only that the two were written
    /// together.
    /// </param>
    /// <param name="allowedDatabases">
    /// The databases this restore may replace, from the panel's own rows. A database the manifest
    /// names and this list does not is refused by the agent and never created.
    /// </param>
    /// <param name="destination">
    /// Where the artifact is read from, already resolved from the backup row's own destination by
    /// <c>BackupDestinationResolver</c>. A restore reads the destination the backup was WRITTEN to,
    /// never the server's current default — the two are the same today and would not be the day a
    /// second destination exists.
    /// </param>
    /// <param name="taskId">The panel task this run's progress is reported onto.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>What the restore did, with the verdict already stated.</returns>
    public async Task<RestoreRunOutcome> RunAsync(
        string accountUsername,
        Guid backupId,
        string expectedSha256,
        IReadOnlyList<string> allowedDatabases,
        AgentBackupDestination destination,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await foreach (var progress in _agent.RestoreAsync(
            accountUsername,
            backupId.ToString(),
            destination,
            expectedSha256,
            allowedDatabases,
            cancellationToken))
        {
            if (progress.Kind == BackupRestoreEventKind.Progress)
            {
                await _tasks.ReportAsync(taskId, (int)progress.Percent, progress.Stage, cancellationToken);
                continue;
            }

            if (progress.Kind != BackupRestoreEventKind.Restored)
            {
                return Failure(RestoreAgentErrorTranslator.ToFailure(progress));
            }

            // The arm that promises an outcome and carries none. Refused rather than read as a
            // restore of nothing: what the account holds now is unknown, and "unknown" must not be
            // recorded under a code that reads as "it did nothing".
            if (progress.Outcome is null)
            {
                return Failure(Error.Of(nameof(ErrorMessages.RestoreInvalidOutcome), ErrorType.Failure));
            }

            return Reduce(progress.Outcome);
        }

        // The sequence ended with no terminal event at all. What the account's home and databases
        // now hold is unknown, so this is a truncation and never a completion.
        return Failure(Error.Of(nameof(ErrorMessages.RestoreTruncated), ErrorType.Failure));
    }

    /// <summary>States the panel's verdict over the agent's three numbers.</summary>
    /// <param name="outcome">What the agent said it did.</param>
    /// <returns>The same facts with the verdict added.</returns>
    /// <remarks>
    /// The comparison is here and nowhere else. A partial restore is given
    /// <c>RestorePartial</c> — an <see cref="ErrorType.Failure"/>, because the SERVER left the
    /// account half-replaced — rather than no error at all, so that a caller reading only the code still
    /// gets an answer that is not "success" — the failure mode being guarded against is a reader who
    /// checks one field and not the other.
    /// </remarks>
    private static RestoreRunOutcome Reduce(AgentRestoreOutcome outcome)
    {
        var whole = outcome.FilesRestored && outcome.DatabasesRestored == outcome.DatabasesTotal;

        return new RestoreRunOutcome(
            whole,
            outcome.FilesRestored,
            outcome.DatabasesRestored,
            outcome.DatabasesTotal,
            whole ? null : Error.Of(nameof(ErrorMessages.RestorePartial), ErrorType.Failure));
    }

    /// <summary>Builds the outcome of a run the agent stated nothing usable about.</summary>
    /// <param name="failure">The machine-stable code naming what went wrong, and its kind.</param>
    /// <returns>
    /// An unsuccessful outcome carrying that error and no figures. The counts are left at zero and
    /// that is NOT a claim that nothing was restored — the agent said nothing, so there is nothing
    /// to report; the code is what the caller is meant to read.
    /// </returns>
    private static RestoreRunOutcome Failure(Error failure)
    {
        return new RestoreRunOutcome(Whole: false, FilesRestored: false, 0, 0, failure);
    }
}
