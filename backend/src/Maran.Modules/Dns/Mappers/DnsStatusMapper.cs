using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Common;

namespace Maran.Modules.Dns.Mappers;

/// <summary>Restates the stored intention and the agent's observation as the status a screen reads.</summary>
public static class DnsStatusMapper
{
    /// <summary>Combines the intention with an observation the agent gave.</summary>
    /// <param name="enabled">What the administrator asked for.</param>
    /// <param name="observed">What the agent observed.</param>
    /// <param name="zoneCount">How many zones the agent reports serving, or zero when it was not asked.</param>
    /// <returns>The status, marked reachable.</returns>
    public static DnsStatusDto Observed(bool enabled, DnsDaemonStatusDto observed, int zoneCount)
    {
        return new DnsStatusDto(
            enabled,
            observed.Running,
            observed.Listening,
            string.Empty,
            zoneCount,
            AgentReachable: true,
            observed.RecursionRefused,
            observed.ResponseRateLimiting);
    }

    /// <summary>Builds the status for an agent that could not be asked.</summary>
    /// <param name="enabled">What the administrator asked for.</param>
    /// <returns>A status whose observed fields are empty and which says so.</returns>
    public static DnsStatusDto Unreachable(bool enabled)
    {
        return new DnsStatusDto(
            enabled, Running: false, Listening: false, Version: string.Empty, ZoneCount: 0, AgentReachable: false);
    }
}
