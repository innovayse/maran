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

    /// <summary>Removing the rule that admits the caller, with another rule left for the port, locks the caller out.</summary>
    [Fact]
    public void Removing_the_rule_that_admits_the_caller_while_another_rule_remains_locks_the_caller_out()
    {
        var verdict = JudgeRemoval(22, "198.51.100.0/24", Rules(("198.51.100.0/24"), ("192.0.2.0/24")));

        Assert.True(verdict.LocksOutCaller);
        Assert.Equal(22, verdict.LostSshPort);
    }

    /// <summary>The inverse control: removing the last rule for the port restores the accept for everyone.</summary>
    [Fact]
    public void Removing_the_last_rule_for_the_port_is_harmless()
    {
        Assert.False(JudgeRemoval(22, "198.51.100.0/24", Rules("198.51.100.0/24")).LocksOutCaller);
    }

    /// <summary>The inverse control: a remaining rule that admits the caller keeps the removal harmless.</summary>
    [Fact]
    public void Removing_a_rule_while_a_remaining_rule_admits_the_caller_is_harmless()
    {
        Assert.False(JudgeRemoval(22, "192.0.2.0/24", Rules("198.51.100.0/24", "192.0.2.0/24")).LocksOutCaller);
        Assert.False(JudgeRemoval(22, "192.0.2.0/24", Rules("0.0.0.0/0", "192.0.2.0/24")).LocksOutCaller);
    }

    /// <summary>A removal naming a rule the host does not hold changes nothing.</summary>
    [Fact]
    public void Removing_a_rule_the_host_does_not_hold_is_harmless()
    {
        Assert.False(JudgeRemoval(22, "203.0.113.0/24", Rules("198.51.100.0/24", "192.0.2.0/24")).LocksOutCaller);
    }

    /// <summary>The removed rule is matched by its canonical source, not by how it was typed.</summary>
    [Fact]
    public void The_removed_rule_is_matched_by_its_canonical_source()
    {
        Assert.True(JudgeRemoval(22, "198.51.100.0/024", Rules("198.51.100.0/24", "192.0.2.0/24")).LocksOutCaller);
    }

    /// <summary>An unreadable caller fails closed on a removal that leaves only restricted rules.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("unknown")]
    public void An_unreadable_caller_is_refused_a_removal_that_leaves_restricted_rules(string caller)
    {
        Assert.True(JudgeRemoval(22, "198.51.100.0/24", Rules("198.51.100.0/24", "192.0.2.0/24"), caller)
            .LocksOutCaller);
    }

    /// <summary>A removal of a UDP rule, a range, or a non-SSH port is never judged.</summary>
    [Fact]
    public void A_removal_that_is_not_a_tcp_single_port_rule_for_an_ssh_port_is_harmless()
    {
        var rules = Rules("198.51.100.0/24", "192.0.2.0/24");

        Assert.False(FirewallLockoutPolicy.JudgeRemoval(
            22, null, AgentFirewallProtocol.Udp, "198.51.100.0/24", rules, SshPorts, Caller).LocksOutCaller);
        Assert.False(FirewallLockoutPolicy.JudgeRemoval(
            22, 23, AgentFirewallProtocol.Tcp, "198.51.100.0/24", rules, SshPorts, Caller).LocksOutCaller);
        Assert.False(FirewallLockoutPolicy.JudgeRemoval(
            8080, null, AgentFirewallProtocol.Tcp, "198.51.100.0/24", rules, SshPorts, Caller).LocksOutCaller);
    }

    /// <summary>Rules for other protocols and ranges on the same number do not count as remaining rules.</summary>
    [Fact]
    public void Remaining_rules_that_are_not_tcp_single_port_rules_for_the_port_do_not_count()
    {
        var rules = new[]
        {
            new AgentFirewallRule(22, null, AgentFirewallProtocol.Tcp, "198.51.100.0/24"),
            new AgentFirewallRule(22, null, AgentFirewallProtocol.Udp, "192.0.2.0/24"),
            new AgentFirewallRule(20, 30, AgentFirewallProtocol.Tcp, "192.0.2.0/24"),
        };

        Assert.False(JudgeRemoval(22, "198.51.100.0/24", rules).LocksOutCaller);
    }

    /// <summary>Banning the caller's own address is detected in every spelling, and another address is not.</summary>
    [Fact]
    public void A_ban_of_the_callers_own_address_is_detected()
    {
        Assert.True(FirewallLockoutPolicy.BansCaller(System.Net.IPAddress.Parse("198.51.100.7"), Caller));
        Assert.True(FirewallLockoutPolicy.BansCaller(
            System.Net.IPAddress.Parse("198.51.100.7"), "::ffff:198.51.100.7"));
        Assert.False(FirewallLockoutPolicy.BansCaller(System.Net.IPAddress.Parse("198.51.100.8"), Caller));
        Assert.False(FirewallLockoutPolicy.BansCaller(System.Net.IPAddress.Parse("198.51.100.7"), ""));
    }

    /// <summary>Builds single-port TCP rules for port 22 from the given sources.</summary>
    /// <param name="sources">The sources of the rules the host is running.</param>
    /// <returns>The rules.</returns>
    private static AgentFirewallRule[] Rules(params string[] sources)
    {
        return [.. sources.Select(source =>
        {
            return new AgentFirewallRule(22, null, AgentFirewallProtocol.Tcp, source);
        })];
    }

    /// <summary>Judges the removal of one TCP rule for a port.</summary>
    /// <param name="port">The port.</param>
    /// <param name="sourceCidr">The removed rule's source.</param>
    /// <param name="existing">The rules the host is running.</param>
    /// <param name="caller">The request's address; the shared caller by default.</param>
    /// <returns>The policy's verdict.</returns>
    private static FirewallLockoutVerdict JudgeRemoval(
        int port,
        string sourceCidr,
        IReadOnlyList<AgentFirewallRule> existing,
        string caller = Caller)
    {
        return FirewallLockoutPolicy.JudgeRemoval(
            port, null, AgentFirewallProtocol.Tcp, sourceCidr, existing, SshPorts, caller);
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
