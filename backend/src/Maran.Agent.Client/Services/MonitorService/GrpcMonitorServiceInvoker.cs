using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>Production <see cref="IMonitorServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcMonitorServiceInvoker : IMonitorServiceInvoker
{
    /// <summary>The generated gRPC client this adapter wraps.</summary>
    private readonly Maran.Agent.V1.MonitorService.MonitorServiceClient _client;

    /// <summary>Wraps <paramref name="client"/> behind the <see cref="IMonitorServiceInvoker"/> seam.</summary>
    /// <param name="client">The generated client to delegate calls to.</param>
    public GrpcMonitorServiceInvoker(Maran.Agent.V1.MonitorService.MonitorServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<GetHostMetricsResponse> GetHostMetricsAsync(
        GetHostMetricsRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.GetHostMetricsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetServiceStatusesResponse> GetServiceStatusesAsync(
        GetServiceStatusesRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.GetServiceStatusesAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetAccountsDiskUsageResponse> GetAccountsDiskUsageAsync(
        GetAccountsDiskUsageRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.GetAccountsDiskUsageAsync(request, cancellationToken: cancellationToken);
    }
}
