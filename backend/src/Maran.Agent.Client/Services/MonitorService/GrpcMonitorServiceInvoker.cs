using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>Production <see cref="IMonitorServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcMonitorServiceInvoker : IMonitorServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="IMonitorServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcMonitorServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.MonitorService.MonitorServiceClient Client
    {
        get { return new Maran.Agent.V1.MonitorService.MonitorServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<GetHostMetricsResponse> GetHostMetricsAsync(
        GetHostMetricsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetHostMetricsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetServiceStatusesResponse> GetServiceStatusesAsync(
        GetServiceStatusesRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetServiceStatusesAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetAccountsDiskUsageResponse> GetAccountsDiskUsageAsync(
        GetAccountsDiskUsageRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetAccountsDiskUsageAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetSftpJailStatusResponse> GetSftpJailStatusAsync(
        GetSftpJailStatusRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetSftpJailStatusAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetQuotaEnforceabilityResponse> GetQuotaEnforceabilityAsync(
        GetQuotaEnforceabilityRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetQuotaEnforceabilityAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetClockSyncResponse> GetClockSyncAsync(
        GetClockSyncRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetClockSyncAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetServerFingerprintInputsResponse> GetServerFingerprintInputsAsync(
        GetServerFingerprintInputsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetServerFingerprintInputsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ListPendingUpdatesResponse> ListPendingUpdatesAsync(
        ListPendingUpdatesRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ListPendingUpdatesAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetRebootRequirementResponse> GetRebootRequirementAsync(
        GetRebootRequirementRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetRebootRequirementAsync(request, cancellationToken: cancellationToken);
    }
}
