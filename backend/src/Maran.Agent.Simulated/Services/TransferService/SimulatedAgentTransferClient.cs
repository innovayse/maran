using System.Runtime.CompilerServices;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.TransferService;
using Maran.Agent.Simulated.Errors;

namespace Maran.Agent.Simulated.Services.TransferService;

/// <summary>
/// The agent's remote-source operations on a simulated server, which opens no connection to any other
/// host: every call answers that the agent does not perform it here.
/// </summary>
/// <remarks>
/// The same answer the simulated mail and backup clients give for what needs a network: the real code, the
/// real kind, and no pretence. A key that could be shown to an administrator and then installed on a
/// machine nobody can reach would be a demonstration of something that cannot work, so even generating a
/// key is refused. The inventory stream ends in its one failed ending, which is what a handler of the real
/// stream already has to cope with.
/// </remarks>
public sealed class SimulatedAgentTransferClient : IAgentTransferClient
{
    /// <inheritdoc/>
    public Task<AgentSourceResult<AgentGeneratedSourceKey>> GenerateSourceKeyAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(AgentSourceResult.Fail<AgentGeneratedSourceKey>(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    public Task<AgentSourceResult<IReadOnlyList<AgentScannedHostKey>>> ScanHostKeyAsync(
        AgentSourceEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            AgentSourceResult.Fail<IReadOnlyList<AgentScannedHostKey>>(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    public Task<AgentSourceResult<AgentTestedSource>> TestSourceAsync(
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(AgentSourceResult.Fail<AgentTestedSource>(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AgentSourceInventoryEvent> ReadSourceInventoryAsync(
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Yield();

        yield return new AgentSourceInventoryFailed(
            SimulatedAgentErrors.NotImplemented(),
            SourceFailureReason.Unspecified);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AgentFilePullEvent> PullAccountFilesAsync(
        AgentFilePullRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Yield();

        yield return new AgentFilePullFailed(
            SimulatedAgentErrors.NotImplemented(),
            SourceFailureReason.Unspecified,
            TransferFailureReason.Unspecified);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AgentDatabaseTransferEvent> StreamRemoteDatabaseAsync(
        AgentDatabaseTransferRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Yield();

        yield return new AgentDatabaseTransferFailed(
            SimulatedAgentErrors.NotImplemented(),
            SourceFailureReason.Unspecified,
            TransferFailureReason.Unspecified);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AgentBackupExportEvent> ExportBackupAsync(
        string accountUsername,
        string backupId,
        Maran.SharedKernel.Security.SensitiveString signingKey,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Yield();

        yield return new AgentBackupExportFailed(SimulatedAgentErrors.NotImplemented());
    }

    /// <inheritdoc/>
    public Task<AgentBackupImportResult> ImportBackupAsync(
        AgentBackupImportHeader header,
        IAsyncEnumerable<ReadOnlyMemory<byte>> chunks,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<AgentBackupImportResult>(
            new AgentBackupImportRefused(SimulatedAgentErrors.NotImplemented(), BackupImportRefusal.Unspecified));
    }
}
