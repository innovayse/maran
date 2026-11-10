using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Mappers;

namespace Maran.Modules.Dns.Tests.Mappers;

/// <summary>The status the DNS screen reads carries the port it asks the administrator to open.</summary>
public sealed class DnsStatusMapperTests
{
    /// <summary>The port is 53, the number the daemon answers on, in an observed status.</summary>
    [Fact]
    public void An_observed_status_names_port_53_for_the_firewall_step()
    {
        var observed = new DnsDaemonStatusDto(
            Running: true,
            Listening: true,
            EnabledAtBoot: true,
            DnsPolicyState.InForce,
            DnsPolicyState.InForce);

        var status = DnsStatusMapper.Observed(enabled: true, observed, zoneCount: 2);

        Assert.Equal(53, status.Port);
        Assert.True(status.AgentReachable);
    }

    /// <summary>The port is still named when the agent could not be asked, so the screen never has to guess it.</summary>
    [Fact]
    public void An_unreachable_status_names_port_53_too()
    {
        var status = DnsStatusMapper.Unreachable(enabled: false);

        Assert.Equal(53, status.Port);
        Assert.False(status.AgentReachable);
    }
}
