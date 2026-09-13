using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.BackupService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Stands in for the agent's backup operations while the account-deletion cascade is exercised end
/// to end, so that the final backup spec §12 promises actually runs against the real
/// <c>AccountBackupService</c>, the real runner and the real PostgreSQL schema.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this fixture had to exist the day the final backup was wired.</b> A deletion now takes a
/// backup before it releases anything, and a failed final backup REFUSES the deletion. With no
/// stand-in the composed test panel reached the real gRPC client, which has no socket to talk to,
/// and every cascade test failed with <c>Unavailable</c> — the account undeletable. That is not a
/// test artefact: it is exactly what a panel whose agent is down now does, and the fixture exists so
/// the cascade tests measure the cascade rather than re-measuring that refusal, which
/// <c>DeleteAccountFinalBackupTests</c> already owns.
/// </para>
/// <para>
/// Only the agent is replaced, and only because it cannot be present: it is a separate root process
/// writing archives on a provisioned host. What the agent does to the HOST is settled by the polygon
/// suite against a real machine, which is the only place it can be.
/// </para>
/// <para>
/// It is deliberately dumb: it replays one created stream and records what it was asked. It asserts
/// nothing itself; the tests do. Restore, list and probe are not reachable from the deletion path
/// and answer as an agent that was never asked would — see each member.
/// </para>
/// </remarks>
public sealed class StubAgentBackupClient : IAgentBackupClient
{
    /// <summary>The size a created artifact reports, so the panel's row is not written from zero.</summary>
    private const ulong SizeBytes = 4096;

    /// <summary>The digest a created artifact reports; a later restore hands this back as expected.</summary>
    private const string Sha256 = "0000000000000000000000000000000000000000000000000000000000000000";

    /// <summary>The account name and backup id of every create, in order.</summary>
    public List<(string AccountUsername, string BackupId)> Creates { get; } = [];

    /// <summary>Records the create and replays a stream that states one completed outcome.</summary>
    /// <param name="accountUsername">The account being backed up.</param>
    /// <param name="backupId">The panel-assigned identifier of the backup.</param>
    /// <param name="destination">Where the artifact is written; recorded by the panel, not by this.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>One progress event and one terminal <c>Created</c> event.</returns>
    public async IAsyncEnumerable<BackupCreateEvent> CreateAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Creates.Add((accountUsername, backupId));

        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();

        yield return new BackupCreateEvent(BackupCreateEventKind.Progress, 50, "archiving_files", 0, string.Empty, 0, null);
        yield return new BackupCreateEvent(BackupCreateEventKind.Created, 100, string.Empty, SizeBytes, Sha256, 0, null);
    }

    /// <summary>
    /// Not reachable from the deletion cascade, and so states nothing rather than a success.
    /// </summary>
    /// <param name="accountUsername">The account being restored into.</param>
    /// <param name="backupId">The backup to restore from.</param>
    /// <param name="destination">Where the artifact resides.</param>
    /// <param name="expectedSha256">The digest the panel recorded.</param>
    /// <param name="allowedDatabases">The databases the panel still knows the account owns.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>An empty stream, which a handler must not read as a restore having happened.</returns>
    /// <remarks>
    /// An empty stream rather than a scripted success: nothing in these tests restores, and a double
    /// that cheerfully answered "restored" would make a cascade that wrongly called it look correct.
    /// </remarks>
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
        yield break;
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

    /// <summary>Reports the artifact as deleted, which is what the cascade's backup rows need.</summary>
    /// <param name="accountUsername">The account owning the backup.</param>
    /// <param name="backupId">The backup whose artifact is removed.</param>
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
