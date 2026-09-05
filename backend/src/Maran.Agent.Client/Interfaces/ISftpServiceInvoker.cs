using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.SftpService.AgentSftpClient"/> and the transport that performs
/// the <c>SftpService</c> calls, so the response-to-<see cref="Result{T}"/> mapping is testable
/// without a real gRPC channel.
/// </summary>
internal interface ISftpServiceInvoker
{
    /// <summary>Invokes <c>CreateSftpUser</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<CreateSftpUserResponse> CreateSftpUserAsync(
        CreateSftpUserRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>SetSftpPassword</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<SetSftpPasswordResponse> SetSftpPasswordAsync(
        SetSftpPasswordRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>DeleteSftpUser</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DeleteSftpUserResponse> DeleteSftpUserAsync(
        DeleteSftpUserRequest request,
        CancellationToken cancellationToken);
}
