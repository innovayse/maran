using Maran.Agent.V1;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.TransferService.AgentTransferClient"/> and the transport that performs
/// the source calls of the <c>TransferService</c>, so the response mapping is testable without a real gRPC
/// channel, including the wire shapes the agent never emits but the contract permits.
/// </summary>
internal interface ITransferServiceInvoker
{
    /// <summary>Invokes <c>GenerateSourceKey</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<GenerateSourceKeyResponse> GenerateSourceKeyAsync(
        GenerateSourceKeyRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ScanHostKey</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof and the failure reason.</returns>
    Task<ScanHostKeyResponse> ScanHostKeyAsync(ScanHostKeyRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>TestSource</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof and the failure reason.</returns>
    Task<TestSourceResponse> TestSourceAsync(TestSourceRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes the server-streaming <c>ReadSourceInventory</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// Progress, account and warning messages followed by exactly one terminal message, when the agent is
    /// well-behaved.
    /// </returns>
    IAsyncEnumerable<ReadSourceInventoryResponse> ReadSourceInventoryAsync(
        ReadSourceInventoryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes the server-streaming <c>ExportBackup</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>The header, the chunks and exactly one terminal message, when the agent is well-behaved.</returns>
    IAsyncEnumerable<ExportBackupResponse> ExportBackupAsync(
        ExportBackupRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes the client-streaming <c>ImportBackup</c>: the header first, then every chunk, then the end of the stream.</summary>
    /// <param name="requests">The wire messages in order: the header, then the chunks.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof and the refusal reason.</returns>
    Task<ImportBackupResponse> ImportBackupAsync(
        IAsyncEnumerable<ImportBackupRequest> requests,
        CancellationToken cancellationToken);

    IAsyncEnumerable<PullAccountFilesResponse> PullAccountFilesAsync(
        PullAccountFilesRequest request,
        CancellationToken cancellationToken);

    IAsyncEnumerable<StreamRemoteDatabaseResponse> StreamRemoteDatabaseAsync(
        StreamRemoteDatabaseRequest request,
        CancellationToken cancellationToken);
}
