using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.FilesService.AgentFilesClient"/> and the transport that performs
/// the <c>FilesService</c> calls, so the response-to-<see cref="Result{T}"/> mapping is testable
/// without a real gRPC channel — the same shape <see cref="ISitesServiceInvoker"/> uses.
/// </summary>
internal interface IFilesServiceInvoker
{
    /// <summary>Invokes the client-streaming <c>WriteFile</c> with a single message.</summary>
    /// <param name="request">The one request carrying both the header and the whole content.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    /// <remarks>
    /// One message, not a stream, because the seam exists for the payloads the panel actually sends —
    /// an ACME challenge token is tens of bytes. A caller with a large file needs a streaming seam,
    /// and adding one is a change here rather than a loop around this method: chunking a file into
    /// repeated single-message calls would truncate it, since each call opens a new stream and the
    /// agent writes what that stream contained.
    /// </remarks>
    Task<WriteFileResponse> WriteFileAsync(WriteFileRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes the client-streaming <c>WriteFile</c> with a header and a body read from a stream.</summary>
    /// <param name="header">The header sent as the first message: account, path, mode, byte cap.</param>
    /// <param name="body">The content to send, read from its current position to its end in chunks.</param>
    /// <param name="cancellationToken">Cancellation for the call; it governs the whole upload.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    /// <remarks>
    /// The body is read once and never rewound, so a caller must not run this under a retry that
    /// replays the call. The agent counts the bytes as they arrive and refuses the upload once the
    /// header's cap is passed, and the response then holds that refusal.
    /// </remarks>
    Task<WriteFileResponse> WriteFileStreamAsync(
        WriteFileHeader header,
        Stream body,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>DeleteEntry</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DeleteEntryResponse> DeleteEntryAsync(DeleteEntryRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>ListDirectory</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ListDirectoryResponse> ListDirectoryAsync(
        ListDirectoryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>SearchEntries</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<SearchEntriesResponse> SearchEntriesAsync(
        SearchEntriesRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes the server-streaming <c>ReadFile</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call; it governs the whole stream, not only its start.</param>
    /// <returns>
    /// The messages the agent streams, in order, each carrying either a chunk or the error that ends
    /// the stream. Disposing the enumerator before the stream is read to its end disposes the
    /// underlying call, which tells the agent to stop reading the file.
    /// </returns>
    IAsyncEnumerable<ReadFileResponse> ReadFileAsync(ReadFileRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>CreateDirectory</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<CreateDirectoryResponse> CreateDirectoryAsync(
        CreateDirectoryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>MoveEntry</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<MoveEntryResponse> MoveEntryAsync(
        MoveEntryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>CopyEntry</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<CopyEntryResponse> CopyEntryAsync(
        CopyEntryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ChangePermissions</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ChangePermissionsResponse> ChangePermissionsAsync(
        ChangePermissionsRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>TrashEntry</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<TrashEntryResponse> TrashEntryAsync(
        TrashEntryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ListTrash</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ListTrashResponse> ListTrashAsync(
        ListTrashRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>RestoreTrashEntry</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<RestoreTrashEntryResponse> RestoreTrashEntryAsync(
        RestoreTrashEntryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>EmptyTrash</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<EmptyTrashResponse> EmptyTrashAsync(
        EmptyTrashRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>PurgeExpiredTrash</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<PurgeExpiredTrashResponse> PurgeExpiredTrashAsync(
        PurgeExpiredTrashRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ForceDeleteEntry</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ForceDeleteEntryResponse> ForceDeleteEntryAsync(
        ForceDeleteEntryRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>CreateArchive</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<CreateArchiveResponse> CreateArchiveAsync(
        CreateArchiveRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ExtractArchive</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ExtractArchiveResponse> ExtractArchiveAsync(
        ExtractArchiveRequest request,
        CancellationToken cancellationToken);
}
