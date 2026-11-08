using Maran.Agent.Client.Services.FirewallService;
using Maran.Modules.Firewall.Domain.Policies;
using Maran.Modules.Firewall.Domain.ValueObjects;

namespace Maran.Modules.Firewall.Tests.Domain.Policies;

/// <summary>
/// When an allow would cost the caller an SSH port, and, as importantly, when it would not.
/// </summary>
/// <remarks>
/// The refusing cases are only worth something beside their inverse controls: a policy mutated to
/// refuse every allow passes every test that hands it a lock-out, so each "harmless" case below is
/// the control for one of the "locks out" cases.
/// </remarks>
public sealed class FirewallLockoutPolicyTests
{
    /// <summary>The SSH ports the host listens on in these tests.</summary>
    private static readonly int[] SshPorts = [22, 2222];

    /// <summary>The address the request arrived from.</summary>
    private const string Caller = "198.51.100.7";

    /// <summary>A single-port TCP allow for an SSH port that excludes the caller is a lock-out.</summary>
    [Fact]
    public void A_tcp_allow_for_an_ssh_port_that_excludes_the_caller_locks_the_caller_out()
    {
        var verdict = Judge(2222, null, AgentFirewallProtocol.Tcp, "203.0.113.0/24");

        Assert.True(verdict.LocksOutCaller);
        Assert.Equal(2222, verdict.LostSshPort);
    }

    /// <summary>The same allow, with the caller inside the range, is harmless.</summary>
    [Fact]
    public void A_tcp_allow_for_an_ssh_port_that_contains_the_caller_is_harmless()
    {
        var verdict = Judge(2222, null, AgentFirewallProtocol.Tcp, "198.51.100.0/24");

        Assert.False(verdict.LocksOutCaller);
        Assert.Null(verdict.LostSshPort);
    }

    /// <summary>A single-host range that is exactly the caller is harmless, and one off by one is not.</summary>
    [Fact]
    public void A_single_host_range_is_judged_on_the_exact_address()
    {
        Assert.False(Judge(22, null, AgentFirewallProtocol.Tcp, "198.51.100.7/32").LocksOutCaller);
        Assert.True(Judge(22, null, AgentFirewallProtocol.Tcp, "198.51.100.8/32").LocksOutCaller);
    }

    /// <summary>An allow open to every source replaces the fallback with an identical accept.</summary>
    [Fact]
    public void An_allow_open_to_every_source_is_harmless_whoever_the_caller_is()
    {
        Assert.False(Judge(22, null, AgentFirewallProtocol.Tcp, "0.0.0.0/0").LocksOutCaller);
        Assert.False(Judge(22, null, AgentFirewallProtocol.Tcp, "0.0.0.0/0", caller: "").LocksOutCaller);
        Assert.False(Judge(22, null, AgentFirewallProtocol.Tcp, "0.0.0.0/0", caller: "2001:db8::1").LocksOutCaller);
    }

    /// <summary>Everyone is 0.0.0.0/0 only: ::/0 restricts to IPv6, so an IPv4 caller is outside it.</summary>
    [Fact]
    public void The_ipv6_any_range_does_not_admit_an_ipv4_caller()
    {
        Assert.True(Judge(22, null, AgentFirewallProtocol.Tcp, "::/0").LocksOutCaller);
        Assert.False(Judge(22, null, AgentFirewallProtocol.Tcp, "::/0", caller: "2001:db8::1").LocksOutCaller);
    }

    /// <summary>An ipv6 caller is matched against an ipv6 range and refused by an ipv4 one.</summary>
    [Fact]
    public void An_ipv6_caller_is_judged_against_the_range_family()
    {
        Assert.False(Judge(22, null, AgentFirewallProtocol.Tcp, "2001:db8::/32", caller: "2001:db8::5").LocksOutCaller);
        Assert.True(Judge(22, null, AgentFirewallProtocol.Tcp, "203.0.113.0/24", caller: "2001:db8::5").LocksOutCaller);
    }

    /// <summary>The mapped spelling the listener reports is read as the IPv4 address it carries.</summary>
    [Fact]
    public void An_ipv4_mapped_caller_is_read_as_its_ipv4_address()
    {
        Assert.False(
            Judge(22, null, AgentFirewallProtocol.Tcp, "198.51.100.0/24", caller: "::ffff:198.51.100.7")
                .LocksOutCaller);
    }

    /// <summary>A caller the panel cannot read is treated as outside every restricted range.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("not an address")]
    public void An_unreadable_caller_is_refused_a_restricted_range_on_an_ssh_port(string caller)
    {
        // Fails closed, unlike the whitelist guard: this one protects reachability, where a wrongly
        // refused allow costs a retry and a wrongly permitted one can cost the server.
        Assert.True(Judge(22, null, AgentFirewallProtocol.Tcp, "198.51.100.0/24", caller: caller).LocksOutCaller);
    }

    /// <summary>A port that is not an SSH port is never guarded, even narrowed to a stranger's range.</summary>
    [Fact]
    public void An_allow_for_a_port_that_is_not_an_ssh_port_is_harmless()
    {
        Assert.False(Judge(8080, null, AgentFirewallProtocol.Tcp, "203.0.113.0/24").LocksOutCaller);
        Assert.False(Judge(23, null, AgentFirewallProtocol.Tcp, "203.0.113.0/24").LocksOutCaller);
    }

    /// <summary>A UDP rule never displaces the TCP fallback, so it cannot lock anyone out.</summary>
    [Fact]
    public void A_udp_allow_for_an_ssh_port_number_is_harmless()
    {
        Assert.False(Judge(22, null, AgentFirewallProtocol.Udp, "203.0.113.0/24").LocksOutCaller);
    }

    /// <summary>A range is an ordinary allow whatever it covers; the SSH fallback stays where it was.</summary>
    [Theory]
    [InlineData(22, 2300)]
    [InlineData(2000, 2222)]
    [InlineData(1, 65535)]
    public void An_allow_over_a_range_that_covers_an_ssh_port_is_harmless(int port, int portTo)
    {
        // ruleset_state.rs render routes only a single TCP port into an SSH group; this test is the
        // panel's half of that dependency, and fails the day the agent's routing stops holding.
        Assert.False(Judge(port, portTo, AgentFirewallProtocol.Tcp, "203.0.113.0/24").LocksOutCaller);
    }

    /// <summary>A range whose lower bound is an SSH port is still a range.</summary>
    [Fact]
    public void A_range_whose_lower_bound_is_an_ssh_port_is_harmless()
    {
        Assert.False(Judge(22, 23, AgentFirewallProtocol.Tcp, "203.0.113.0/24").LocksOutCaller);
    }

    /// <summary>A rule added to a port that already has rules only widens who is admitted.</summary>
    [Fact]
    public void An_allow_next_to_an_existing_rule_for_the_port_is_harmless()
    {
        var existing = new[] { new AgentFirewallRule(2222, null, AgentFirewallProtocol.Tcp, "192.0.2.0/24") };

        Assert.False(Judge(2222, null, AgentFirewallProtocol.Tcp, "203.0.113.0/24", existing).LocksOutCaller);
    }

    /// <summary>Rules for other ports, other protocols and ranges do not stand in for this port's rules.</summary>
    [Fact]
    public void Existing_rules_that_are_not_tcp_single_port_rules_for_this_port_do_not_count()
    {
        var existing = new[]
        {
            new AgentFirewallRule(22, null, AgentFirewallProtocol.Tcp, "192.0.2.0/24"),
            new AgentFirewallRule(2222, null, AgentFirewallProtocol.Udp, "192.0.2.0/24"),
            new AgentFirewallRule(2000, 2300, AgentFirewallProtocol.Tcp, "192.0.2.0/24"),
            new AgentFirewallRule(8080, null, AgentFirewallProtocol.Tcp, "0.0.0.0/0"),
        };

        Assert.True(Judge(2222, null, AgentFirewallProtocol.Tcp, "203.0.113.0/24", existing).LocksOutCaller);
    }

    /// <summary>Every SSH port is guarded, not only the first.</summary>
    [Fact]
    public void Every_ssh_port_of_the_host_is_guarded()
    {
        Assert.Equal(22, Judge(22, null, AgentFirewallProtocol.Tcp, "203.0.113.0/24").LostSshPort);
        Assert.Equal(2222, Judge(2222, null, AgentFirewallProtocol.Tcp, "203.0.113.0/24").LostSshPort);
    }

    /// <summary>The range is read in its canonical spelling, so a differently written everyone is still everyone.</summary>
    [Fact]
    public void A_range_is_judged_by_what_it_installs_not_by_how_it_was_written()
    {
        Assert.False(Judge(22, null, AgentFirewallProtocol.Tcp, "0.0.0.0/00").LocksOutCaller);
        Assert.False(Judge(22, null, AgentFirewallProtocol.Tcp, "198.51.100.0/024").LocksOutCaller);
    }

    /// <summary>Judges one allow with the host and caller these tests share.</summary>
    /// <param name="port">The allow's port or lower bound.</param>
    /// <param name="portTo">The allow's upper bound, or null.</param>
    /// <param name="protocol">The allow's protocol.</param>
    /// <param name="sourceCidr">The allow's source range.</param>
    /// <param name="existing">The rules the host is running; none by default.</param>
    /// <param name="caller">The request's address; the shared caller by default.</param>
    /// <returns>The policy's verdict.</returns>
    private static FirewallLockoutVerdict Judge(
        int port,
        int? portTo,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<AgentFirewallRule>? existing = null,
        string caller = Caller)
    {
        return FirewallLockoutPolicy.Judge(port, portTo, protocol, sourceCidr, existing ?? [], SshPorts, caller);
    }
}
