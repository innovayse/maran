using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>The monitoring agent, answering whatever a test says it answers.</summary>
/// <remarks>
/// Substituted because the real client talks to a separate root process over a unix socket, which no
/// test host has. Everything else in these tests — the HTTP surface, the authorization policy, the
/// PostgreSQL the samples live in — is real.
/// </remarks>
public sealed class StubAgentMonitorClient : IAgentMonitorClient
{
    /// <summary>What a request for the host's metrics is answered with.</summary>
    public Result<AgentHostMetrics> Metrics { get; set; } =
        Result<AgentHostMetrics>.Ok(new AgentHostMetrics(7.5, 2_048, 8_192, 40, 100, 1_000, 2_000, 0.4, 0.3, 0.2));

    /// <summary>What a request for the service statuses is answered with.</summary>
    public Result<IReadOnlyList<AgentServiceStatus>> Statuses { get; set; } =
        Result<IReadOnlyList<AgentServiceStatus>>.Ok(
        [
            new AgentServiceStatus(AgentManagedService.WebServer, AgentServiceState.Running, "active (running)"),
            new AgentServiceStatus(AgentManagedService.Ssh, AgentServiceState.Unknown, "ssh.socket is listening"),
        ]);

    /// <summary>What a request for per-account disk usage is answered with.</summary>
    public Result<IReadOnlyList<AgentAccountDiskUsage>> DiskUsage { get; set; } =
        Result<IReadOnlyList<AgentAccountDiskUsage>>.Ok([]);

    /// <inheritdoc />
    public Task<Result<AgentHostMetrics>> GetHostMetricsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Metrics);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentServiceStatus>>> GetServiceStatusesAsync(CancellationToken cancellationToken)
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
