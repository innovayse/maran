using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.FtpsService.AgentFtpsClient"/> and the transport that performs
/// the <c>FtpsService</c> calls, so the response-to-<see cref="Result{T}"/> mapping is testable
/// without a real gRPC channel.
/// </summary>
internal interface IFtpsServiceInvoker
{
    /// <summary>Invokes <c>EnableFtps</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<EnableFtpsResponse> EnableFtpsAsync(
        EnableFtpsRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>DisableFtps</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DisableFtpsResponse> DisableFtpsAsync(
        DisableFtpsRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>GetFtpsStatus</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<GetFtpsStatusResponse> GetFtpsStatusAsync(
        GetFtpsStatusRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ReloadFtpsTls</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ReloadFtpsTlsResponse> ReloadFtpsTlsAsync(
        ReloadFtpsTlsRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>CreateFtpsUser</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<CreateFtpsUserResponse> CreateFtpsUserAsync(
        CreateFtpsUserRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>SetFtpsPassword</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<SetFtpsPasswordResponse> SetFtpsPasswordAsync(
        SetFtpsPasswordRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>DeleteFtpsUser</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DeleteFtpsUserResponse> DeleteFtpsUserAsync(
        DeleteFtpsUserRequest request,
        CancellationToken cancellationToken);
}
