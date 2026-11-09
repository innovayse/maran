using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A monitoring agent that counts how often the host's readings were asked for, and otherwise answers
/// as <see cref="StubAgentMonitorClient"/> does.
/// </summary>
/// <remarks>
/// The dashboard stream's cost promise is a statement about this one call: however many streams are
/// open, the agent is asked once per tick, not once per stream. A counter on the wrapper is the only
/// thing that can observe that from outside the process under test. Every other member forwards to the
/// inner stub unchanged.
/// </remarks>
public sealed class CountingAgentMonitorClient : IAgentMonitorClient
{
    /// <summary>The stub every call is forwarded to.</summary>
    private readonly StubAgentMonitorClient _inner = new();

    /// <summary>How many times the host metrics were asked for.</summary>
    private int _metricsCalls;

    /// <summary>How many times the host metrics were asked for.</summary>
    public int MetricsCalls
    {
        get
        {
            return Volatile.Read(ref _metricsCalls);
        }
    }

    /// <inheritdoc />
    public Task<Result<AgentHostMetrics>> GetHostMetricsAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _metricsCalls);
        return _inner.GetHostMetricsAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentServiceStatus>>> GetServiceStatusesAsync(CancellationToken cancellationToken)
    {
        return _inner.GetServiceStatusesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentAccountDiskUsage>>> GetAccountsDiskUsageAsync(
        IReadOnlyCollection<string> accountUsernames,
        CancellationToken cancellationToken)
    {
        return _inner.GetAccountsDiskUsageAsync(accountUsernames, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<AgentSftpJailStatus>> GetSftpJailStatusAsync(CancellationToken cancellationToken)
    {
        return _inner.GetSftpJailStatusAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<AgentQuotaEnforceability>> GetQuotaEnforceabilityAsync(CancellationToken cancellationToken)
    {
        return _inner.GetQuotaEnforceabilityAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<AgentClockSyncState>> GetClockSyncAsync(CancellationToken cancellationToken)
    {
        return _inner.GetClockSyncAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<AgentServerFingerprint>> GetServerFingerprintAsync(CancellationToken cancellationToken)
    {
        return _inner.GetServerFingerprintAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentPendingUpdate>>> ListPendingUpdatesAsync(
        CancellationToken cancellationToken)
    {
        return _inner.ListPendingUpdatesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<AgentRebootRequirement>> GetRebootRequirementAsync(CancellationToken cancellationToken)
    {
        return _inner.GetRebootRequirementAsync(cancellationToken);
    }
}
