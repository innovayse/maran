using Maran.Agent.Client.Services.MonitorService;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The panel's read-only view of the host: resource metrics, the state of the services the agent
/// watches, and per-account disk use. Nothing here changes anything on the server.
/// </summary>
public interface IAgentMonitorClient
{
    /// <summary>Reads a point-in-time snapshot of host resource metrics.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The snapshot, or a typed failure.</returns>
    /// <remarks>
    /// The two network figures are counters since boot rather than per-interval totals, so a caller
    /// showing a rate derives it from two readings and the seconds that actually elapsed between
    /// them.
    /// </remarks>
    Task<Result<AgentHostMetrics>> GetHostMetricsAsync(CancellationToken cancellationToken);

    /// <summary>Reads the state of the services the agent watches.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>
    /// One row per service the agent actually observes, or a typed failure. The set is smaller than
    /// the service enum, and a service with no row is one this agent does not watch: read its
    /// absence as <see cref="AgentServiceState.Unknown"/>, never as "not running".
    /// </returns>
    /// <remarks>
    /// The state is three-valued, and the third value is the point of it. "Not known" covers a
    /// socket-activated unit nothing has connected to yet, a unit mid-transition and a unit not
    /// installed on this host — none of which is an outage. A caller that alerts on anything but
    /// <see cref="AgentServiceState.Stopped"/> will e-mail about an outage on every Debian-family
    /// host at every reboot, because the SSH unit there is a socket that starts nothing until the
    /// first connection.
    /// </remarks>
    Task<Result<IReadOnlyList<AgentServiceStatus>>> GetServiceStatusesAsync(CancellationToken cancellationToken);

    /// <summary>Reads how much disk each hosting account is using.</summary>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>One row per account, carrying used bytes only, or a typed failure.</returns>
    /// <remarks>
    /// Used bytes, never a quota: the quota is the panel's own data, chosen when the account was
    /// created and stored by the Accounts module. Comparing the two is the caller's job, and both
    /// halves of that comparison must come from the side that owns them.
    /// </remarks>
    Task<Result<IReadOnlyList<AgentAccountDiskUsage>>> GetAccountsDiskUsageAsync(
        CancellationToken cancellationToken);
}
