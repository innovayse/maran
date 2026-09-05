using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>The agent, answering whatever a test says it answers.</summary>
/// <remarks>
/// A stub rather than the real client because the real one talks to a separate root process over a
/// unix socket. What the tests here care about is the two ANSWERS the sampler must handle
/// differently — a reading, and a refusal — which are exactly what this can be told to give.
/// </remarks>
public sealed class StubAgentMonitorClient : IAgentMonitorClient
{
    /// <summary>How many times the host metrics were asked for.</summary>
    public int MetricsCalls { get; private set; }

    /// <summary>What <see cref="GetHostMetricsAsync"/> answers.</summary>
    public Result<AgentHostMetrics> Metrics { get; set; } =
        Result<AgentHostMetrics>.Ok(new AgentHostMetrics(5, 100, 1_000, 200, 1_000, 10, 20, 0.1, 0.2, 0.3));

    /// <summary>What <see cref="GetServiceStatusesAsync"/> answers.</summary>
    public Result<IReadOnlyList<AgentServiceStatus>> Statuses { get; set; } =
        Result<IReadOnlyList<AgentServiceStatus>>.Ok([]);

    /// <summary>What <see cref="GetAccountsDiskUsageAsync"/> answers.</summary>
    public Result<IReadOnlyList<AgentAccountDiskUsage>> DiskUsage { get; set; } =
        Result<IReadOnlyList<AgentAccountDiskUsage>>.Ok([]);

    /// <inheritdoc />
    public Task<Result<AgentHostMetrics>> GetHostMetricsAsync(CancellationToken cancellationToken)
    {
        MetricsCalls++;
        return Task.FromResult(Metrics);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentServiceStatus>>> GetServiceStatusesAsync(
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Statuses);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentAccountDiskUsage>>> GetAccountsDiskUsageAsync(
        CancellationToken cancellationToken)
    {
        return Task.FromResult(DiskUsage);
    }
}
