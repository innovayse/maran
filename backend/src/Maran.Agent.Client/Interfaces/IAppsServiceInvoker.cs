using Maran.Agent.V1;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.AppsService.AgentAppsClient"/> and the transport that performs the
/// <c>AppsService</c> calls, so the response-to-<see cref="Maran.SharedKernel.Results.Result{T}"/> mapping
/// is testable without a real gRPC channel.
/// </summary>
internal interface IAppsServiceInvoker
{
    /// <summary>Invokes <c>ListApps</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ListAppsResponse> ListAppsAsync(ListAppsRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes <c>ListInstallableApps</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ListInstallableAppsResponse> ListInstallableAppsAsync(
        ListInstallableAppsRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes the server-streaming <c>InstallApp</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// The raw wire responses in order: progress messages followed by exactly one terminal message.
    /// A sequence that ends without one is an install whose outcome is unknown.
    /// </returns>
    IAsyncEnumerable<InstallAppResponse> InstallAppAsync(InstallAppRequest request, CancellationToken cancellationToken);

    /// <summary>Invokes the server-streaming <c>UninstallApp</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the stream.</param>
    /// <returns>
    /// The raw wire responses in order: progress messages followed by exactly one terminal message.
    /// A sequence that ends without one is a removal whose outcome is unknown.
    /// </returns>
    IAsyncEnumerable<UninstallAppResponse> UninstallAppAsync(
        UninstallAppRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ControlAppService</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ControlAppServiceResponse> ControlAppServiceAsync(
        ControlAppServiceRequest request,
        CancellationToken cancellationToken);
}
