using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.MonitorService;

/// <summary>Stub of <c>IMonitorServiceInvoker</c> returning canned responses and keeping every request.</summary>
/// <remarks>
/// The three requests here carry no fields at all, so what the captured ones prove is only that the
/// call was made — which is worth asserting, because a client that answered a read from nothing at
/// all would otherwise look identical from the outside.
/// </remarks>
internal sealed class StubMonitorService : IMonitorServiceInvoker
{
    /// <summary>Response returned from <see cref="GetHostMetricsAsync"/>.</summary>
    public GetHostMetricsResponse MetricsResponse { get; set; } = new();

    /// <summary>The last metrics request the stub received, for asserting the call was made.</summary>
    public GetHostMetricsRequest? LastMetricsRequest { get; private set; }

    /// <summary>Response returned from <see cref="GetServiceStatusesAsync"/>.</summary>
    public GetServiceStatusesResponse StatusesResponse { get; set; } = new();

    /// <summary>The last statuses request the stub received, for asserting the call was made.</summary>
    public GetServiceStatusesRequest? LastStatusesRequest { get; private set; }

    /// <summary>Response returned from <see cref="GetAccountsDiskUsageAsync"/>.</summary>
    public GetAccountsDiskUsageResponse DiskUsageResponse { get; set; } = new();

    /// <summary>The last disk-usage request the stub received, for asserting the call was made.</summary>
    public GetAccountsDiskUsageRequest? LastDiskUsageRequest { get; private set; }

    /// <summary>Builds a stub answering the statuses call with one service in one state.</summary>
    /// <param name="service">Which service the row describes.</param>
    /// <param name="state">The state the agent reports.</param>
    /// <param name="running">The deprecated boolean the agent still writes beside it.</param>
    /// <param name="detail">The service manager's own words.</param>
    /// <returns>The configured stub.</returns>
    public static StubMonitorService ReportingOneService(
        ManagedService service,
        ServiceState state,
        bool running,
        string detail)
    {
        return new StubMonitorService
        {
            StatusesResponse = new GetServiceStatusesResponse
            {
                Ok = new GetServiceStatusesOk
                {
                    Services =
                    {
                        new ServiceStatus
                        {
                            Service = service,
                            State = state,
                            Running = running,
                            Detail = detail,
                        },
                    },
                },
            },
        };
    }

    /// <inheritdoc/>
    public Task<GetHostMetricsResponse> GetHostMetricsAsync(
        GetHostMetricsRequest request,
        CancellationToken cancellationToken)
    {
        LastMetricsRequest = request;
        return Task.FromResult(MetricsResponse);
    }

    /// <inheritdoc/>
    public Task<GetServiceStatusesResponse> GetServiceStatusesAsync(
        GetServiceStatusesRequest request,
        CancellationToken cancellationToken)
    {
        LastStatusesRequest = request;
        return Task.FromResult(StatusesResponse);
    }

    /// <inheritdoc/>
    public Task<GetAccountsDiskUsageResponse> GetAccountsDiskUsageAsync(
        GetAccountsDiskUsageRequest request,
        CancellationToken cancellationToken)
    {
        LastDiskUsageRequest = request;
        return Task.FromResult(DiskUsageResponse);
    }
}
