using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.PhpService.AgentPhpClient"/> and the transport that performs the
/// <c>PhpService</c> calls, so the response-to-<see cref="Result{T}"/> mapping is testable without a
/// real gRPC channel.
/// </summary>
internal interface IPhpServiceInvoker
{
    /// <summary>Invokes <c>ListPhpVersions</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ListPhpVersionsResponse> ListPhpVersionsAsync(
        ListPhpVersionsRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes the server-streaming <c>InstallPhpVersion</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// The raw wire responses in order: progress messages followed by exactly one terminal message.
    /// A sequence that ends without one is an install whose outcome is unknown.
    /// </returns>
    IAsyncEnumerable<InstallPhpVersionResponse> InstallPhpVersionAsync(
        InstallPhpVersionRequest request,
        CancellationToken cancellationToken);
}
