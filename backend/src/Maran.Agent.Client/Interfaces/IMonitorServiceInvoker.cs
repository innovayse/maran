using Maran.Agent.V1;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.MonitorService.AgentMonitorClient"/> and the transport that
/// performs the <c>MonitorService</c> calls, so the response-to-<see cref="Result{T}"/> mapping is
/// testable without a real gRPC channel.
/// </summary>
internal interface IMonitorServiceInvoker
{
    /// <summary>Invokes <c>GetHostMetrics</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<GetHostMetricsResponse> GetHostMetricsAsync(
        GetHostMetricsRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>GetServiceStatuses</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<GetServiceStatusesResponse> GetServiceStatusesAsync(
        GetServiceStatusesRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>GetAccountsDiskUsage</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<GetAccountsDiskUsageResponse> GetAccountsDiskUsageAsync(
        GetAccountsDiskUsageRequest request,
        CancellationToken cancellationToken);
}
