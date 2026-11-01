using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.SslService.AgentSslClient"/> and the transport that performs the
/// <c>SslService</c> calls, so the response-to-<see cref="Result{T}"/> mapping is testable without a
/// real gRPC channel.
/// </summary>
internal interface ISslServiceInvoker
{
    /// <summary>Invokes <c>InstallCertificate</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<InstallCertificateResponse> InstallCertificateAsync(
        InstallCertificateRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>RemoveCertificate</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<RemoveCertificateResponse> RemoveCertificateAsync(
        RemoveCertificateRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>InstallPanelCertificate</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying one branch of its oneof.</returns>
    Task<InstallPanelCertificateResponse> InstallPanelCertificateAsync(
        InstallPanelCertificateRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>WritePanelChallenge</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<WritePanelChallengeResponse> WritePanelChallengeAsync(
        WritePanelChallengeRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>RemovePanelChallenge</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<RemovePanelChallengeResponse> RemovePanelChallengeAsync(
        RemovePanelChallengeRequest request,
        CancellationToken cancellationToken);
}
