using Maran.Agent.V1;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.BackupService.AgentBackupClient"/> and the transport that
/// performs the <c>BackupService</c> calls, so the response mapping is testable without a real gRPC
/// channel — including the wire shapes the agent never emits but the contract permits.
/// </summary>
internal interface IBackupServiceInvoker
{
    /// <summary>Invokes the server-streaming <c>CreateBackup</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>Progress messages followed by exactly one terminal message, when the agent is well-behaved.</returns>
    IAsyncEnumerable<CreateBackupResponse> CreateBackupAsync(
        CreateBackupRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes the server-streaming <c>RestoreBackup</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>Progress messages followed by exactly one terminal message, when the agent is well-behaved.</returns>
    IAsyncEnumerable<RestoreBackupResponse> RestoreBackupAsync(
        RestoreBackupRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ListBackups</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ListBackupsResponse> ListBackupsAsync(ListBackupsRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>DeleteBackup</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DeleteBackupResponse> DeleteBackupAsync(DeleteBackupRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>ProbeDestination</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ProbeDestinationResponse> ProbeDestinationAsync(
        ProbeDestinationRequest request,
        CancellationToken cancellationToken);
}
