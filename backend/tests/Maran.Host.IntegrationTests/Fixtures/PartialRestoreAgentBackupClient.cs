using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.BackupService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for the agent's backup operations while a RESTORE is driven over real HTTP to its
/// partial ending: the restore stream states one terminal outcome whose figures do not add up to a
/// whole restore, which is the one ending the composed panel must answer with the measured counts
/// in the problem response's <c>restore</c> extension.
/// </summary>
/// <remarks>
/// A partial restore is unreachable through the real agent — it rolls back and reports a failure —
/// so this seam is the only place the composed panel's answer to one can be observed at all
/// (rules/testing.md: the check must be able to observe what it reports on; the agent's own
/// rollback is the polygon suite's to prove). It is deliberately dumb: it replays the scripted
/// terminal outcome and answers every other operation as an agent that was never asked would.
/// </remarks>
public sealed class PartialRestoreAgentBackupClient : IAgentBackupClient
{
    /// <summary>The terminal outcome every restore stream states.</summary>
    private readonly AgentRestoreOutcome _outcome;

    /// <summary>Creates the stand-in.</summary>
    /// <param name="outcome">The terminal outcome every restore stream will state.</param>
    public PartialRestoreAgentBackupClient(AgentRestoreOutcome outcome)
    {
        _outcome = outcome;
    }

    /// <summary>Not reachable from these tests; answers an empty stream rather than a scripted success.</summary>
    /// <param name="accountUsername">The account being backed up.</param>
    /// <param name="backupId">The panel-assigned identifier of the backup.</param>
    /// <param name="destination">Where the artifact would be written.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>An empty stream, which a create path must not read as a backup having happened.</returns>
    public async IAsyncEnumerable<BackupCreateEvent> CreateAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();
        yield break;
    }

    /// <summary>Replays one restore stream ending in the scripted terminal outcome.</summary>
    /// <param name="accountUsername">The account being restored into.</param>
    /// <param name="backupId">The backup to restore from.</param>
    /// <param name="destination">Where the artifact resides.</param>
    /// <param name="expectedSha256">The digest the panel recorded.</param>
    /// <param name="allowedDatabases">The databases the panel still knows the account owns.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>One progress event, then the scripted terminal outcome.</returns>
    public async IAsyncEnumerable<BackupRestoreEvent> RestoreAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        string expectedSha256,
        IReadOnlyList<string> allowedDatabases,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();

        yield return new BackupRestoreEvent(
            BackupRestoreEventKind.Progress, 50, "loading_databases", null, null);
        yield return new BackupRestoreEvent(
            BackupRestoreEventKind.Restored, 100, string.Empty, _outcome, null);
    }

    /// <summary>Answers an empty listing; nothing in these tests lists.</summary>
    /// <param name="accountUsername">The account whose backups would be listed.</param>
    /// <param name="destination">The destination the listing is about.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>An empty listing.</returns>
    public Task<Result<IReadOnlyList<AgentBackupSummary>>> ListAsync(
        string accountUsername,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<IReadOnlyList<AgentBackupSummary>>.Ok([]));
    }

    /// <summary>Reports the artifact as deleted; nothing in these tests deletes.</summary>
    /// <param name="accountUsername">The account owning the backup.</param>
    /// <param name="backupId">The backup whose artifact would be removed.</param>
    /// <param name="destination">Where the artifact resides.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>Success.</returns>
    public Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <summary>Answers as the shipping agent does: no probe is performed at all.</summary>
    /// <param name="destination">The destination that would be probed.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>A not-implemented failure, which is never permission to save a destination.</returns>
    public Task<Result<AgentPublicReadVerdict>> ProbeAsync(
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            Result<AgentPublicReadVerdict>.Fail(Error.Of("AgentNotImplemented", ErrorType.Failure)));
    }
}
