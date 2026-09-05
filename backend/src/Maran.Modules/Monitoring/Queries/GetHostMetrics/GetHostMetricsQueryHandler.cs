using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Common;

namespace Maran.Modules.Monitoring.Queries.GetHostMetrics;

/// <summary>Handles <see cref="GetHostMetricsQuery"/> by asking the agent for one reading.</summary>
/// <remarks>
/// Live from the agent rather than from the newest stored sample. The dashboard's question is "what
/// is this machine doing", which the table cannot answer without lagging by up to a whole sampling
/// interval — and cannot answer at all on a panel whose sampler has not run yet, which is every
/// panel for its first minute.
///
/// Nothing is derived here. In particular the two network counters are passed through as counters:
/// turning a pair of them into a rate needs the seconds between two readings, and there is only one.
/// </remarks>
public sealed class GetHostMetricsQueryHandler
{
    /// <summary>The agent, which is the only thing in the system that can see the host's resources.</summary>
    private readonly IAgentMonitorClient _agent;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent client that reads the host.</param>
    public GetHostMetricsQueryHandler(IAgentMonitorClient agent)
    {
        _agent = agent;
    }

    /// <summary>Returns one live reading of the host.</summary>
    /// <param name="query">The (parameterless) read request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The reading, or the agent's own typed failure.</returns>
    public async Task<Result<HostMetricsDto>> HandleAsync(
        GetHostMetricsQuery query,
        CancellationToken cancellationToken)
    {
        var metrics = await _agent.GetHostMetricsAsync(cancellationToken);

        if (!metrics.IsSuccess)
        {
            return Result<HostMetricsDto>.Fail(metrics.Error!);
        }

        return Result<HostMetricsDto>.Ok(ToDto(metrics.Value));
    }

    /// <summary>Projects the agent's reading onto the panel's read model.</summary>
    /// <param name="metrics">The reading the agent returned.</param>
    /// <returns>The same figures, with the byte counts narrowed to the panel's signed representation.</returns>
    /// <remarks>
    /// The narrowing saturates rather than casting, for the reason <c>MetricsSampler</c> states at
    /// length: an unchecked cast of a value above <see cref="long.MaxValue"/> produces a NEGATIVE
    /// byte count, and a dashboard showing minus eight exabytes of memory is worse than one showing
    /// a ceiling nothing will reach.
    /// </remarks>
    private static HostMetricsDto ToDto(AgentHostMetrics metrics)
    {
        return new HostMetricsDto(
            metrics.CpuPercent,
            ToSignedBytes(metrics.MemoryUsedBytes),
            ToSignedBytes(metrics.MemoryTotalBytes),
            ToSignedBytes(metrics.DiskUsedBytes),
            ToSignedBytes(metrics.DiskTotalBytes),
            ToSignedBytes(metrics.NetworkRxBytes),
            ToSignedBytes(metrics.NetworkTxBytes),
            metrics.LoadAverage1m,
            metrics.LoadAverage5m,
            metrics.LoadAverage15m);
    }

    /// <summary>Narrows an unsigned byte count onto the signed one the panel carries.</summary>
    /// <param name="value">The figure the agent reported.</param>
    /// <returns>The same value, saturated at <see cref="long.MaxValue"/>.</returns>
    private static long ToSignedBytes(ulong value)
    {
        return value > long.MaxValue ? long.MaxValue : (long)value;
    }
}
