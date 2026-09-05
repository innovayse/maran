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

    /// <summary>Invokes <c>DeleteEntry</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DeleteEntryResponse> DeleteEntryAsync(DeleteEntryRequest request, CancellationToken cancellationToken);
}
