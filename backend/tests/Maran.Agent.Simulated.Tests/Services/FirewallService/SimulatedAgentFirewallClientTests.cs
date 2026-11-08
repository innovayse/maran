using Maran.Agent.Client.Services.FirewallService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Services.FirewallService;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Tests.TestSupport;

namespace Maran.Agent.Simulated.Tests.Services.FirewallService;

/// <summary>The simulated agent's firewall operations answer like the real agent's and enforce nothing.</summary>
public sealed class SimulatedAgentFirewallClientTests
{
    /// <summary>The addresses banned in the listing test, in the order they are listed.</summary>
    private static readonly string[] BannedAddresses = ["198.51.100.1", "203.0.113.9"];

    /// <summary>Any source, the value the panel sends for an unrestricted rule.</summary>
    private const string AnySource = "0.0.0.0/0";

    /// <summary>The panel's public port in every test that sends one.</summary>
    private const int PanelPort = 8443;

    /// <summary>The ports this imaginary host's sshd listens on.</summary>
    private static readonly int[] SshPorts = [22, 2222];

    /// <summary>An allowed port is listed, in the order rules were allowed.</summary>
    [Fact]
    public async Task An_allowed_port_is_listed_in_the_order_rules_were_allowed()
    {
        var (client, _) = NewClient();
        await client.AllowPortAsync(
            443,
            null,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);
        await client.AllowPortAsync(
            53,
            null,
            AgentFirewallProtocol.Udp,
            "10.0.0.0/8",
            SshPorts,
            PanelPort,
            CancellationToken.None);

        var listed = await client.ListRulesAsync(SshPorts, PanelPort, CancellationToken.None);

        Assert.Equal(
            new[]
            {
                new AgentFirewallRule(443, null, AgentFirewallProtocol.Tcp, AnySource),
                new AgentFirewallRule(53, null, AgentFirewallProtocol.Udp, "10.0.0.0/8"),
            },
            listed.Value);
    }

    /// <summary>Allowing an identical rule twice answers already exists and lists it once.</summary>
    [Fact]
    public async Task Allowing_an_identical_rule_twice_answers_already_exists_and_lists_it_once()
    {
        var (client, _) = NewClient();
        await client.AllowPortAsync(
            8080,
            8090,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        var again = await client.AllowPortAsync(
            8080,
            8090,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.AlreadyExistsCode, again.Error!.Code);
        Assert.Single((await client.ListRulesAsync(SshPorts, PanelPort, CancellationToken.None)).Value);
    }

    /// <summary>
    /// The same port with another protocol, another source or a range ending elsewhere is another rule.
    /// </summary>
    [Fact]
    public async Task The_same_port_with_another_protocol_source_or_range_is_another_rule()
    {
        var (client, _) = NewClient();
        await client.AllowPortAsync(
            8080,
            null,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        var udp = await client.AllowPortAsync(
            8080,
            null,
            AgentFirewallProtocol.Udp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);
        var source = await client.AllowPortAsync(
            8080,
            null,
            AgentFirewallProtocol.Tcp,
            "10.0.0.0/8",
            SshPorts,
            PanelPort,
            CancellationToken.None);
        var range = await client.AllowPortAsync(
            8080,
            8081,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        Assert.True(udp.IsSuccess);
        Assert.True(source.IsSuccess);
        Assert.True(range.IsSuccess);
    }

    /// <summary>
    /// A source range is kept in its canonical spelling, so a differently written equal range is the same rule.
    /// </summary>
    [Fact]
    public async Task A_source_range_is_kept_in_its_canonical_spelling()
    {
        var (client, _) = NewClient();
        await client.AllowPortAsync(
            8080,
            null,
            AgentFirewallProtocol.Tcp,
            "::ffff:10.0.0.0/104",
            SshPorts,
            PanelPort,
            CancellationToken.None);

        var again = await client.AllowPortAsync(
            8080,
            null,
            AgentFirewallProtocol.Tcp,
            "10.0.0.0/8",
            SshPorts,
            PanelPort,
            CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.AlreadyExistsCode, again.Error!.Code);
    }

    /// <summary>
    /// A port outside 1-65535, an inverted or equal range, or a source that is not a range is invalid input.
    /// </summary>
    [Theory]
    [InlineData(0, null, AnySource)]
    [InlineData(65536, null, AnySource)]
    [InlineData(8080, 8080, AnySource)]
    [InlineData(8080, 8000, AnySource)]
    [InlineData(8080, 70000, AnySource)]
    [InlineData(8080, null, "not-a-cidr")]
    [InlineData(8080, null, "10.0.0.1/8")]
    [InlineData(8080, null, "")]
    public async Task An_unusable_port_range_or_source_is_invalid_input_and_installs_nothing(
        int port,
        int? portTo,
        string source)
    {
        var (client, _) = NewClient();

        var allowed = await client.AllowPortAsync(
            port,
            portTo,
            AgentFirewallProtocol.Tcp,
            source,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.InvalidInputCode, allowed.Error!.Code);
        Assert.Empty((await client.ListRulesAsync(SshPorts, PanelPort, CancellationToken.None)).Value);
    }

    /// <summary>
    /// Host ports that are missing or unusable are a misconfiguration, never invalid input, and change nothing.
    /// </summary>
    [Fact]
    public async Task Unusable_host_ports_are_a_misconfiguration_and_change_nothing()
    {
        var (client, _) = NewClient();

        var noSsh = await client.AllowPortAsync(
            443,
            null,
            AgentFirewallProtocol.Tcp,
            AnySource,
            [],
            PanelPort,
            CancellationToken.None);
        var badSsh = await client.AllowPortAsync(
            443,
            null,
            AgentFirewallProtocol.Tcp,
            AnySource,
            [22, 0],
            PanelPort,
            CancellationToken.None);
        var noPanel = await client.AllowPortAsync(
            443,
            null,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            0,
            CancellationToken.None);
        var denied = await client.DenyPortAsync(
            443,
            null,
            AgentFirewallProtocol.Tcp,
            AnySource,
            [],
            PanelPort,
            CancellationToken.None);
        var listed = await client.ListRulesAsync([], PanelPort, CancellationToken.None);
        var listedNoPanel = await client.ListRulesAsync(SshPorts, 0, CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.FirewallPortsMisconfiguredCode, noSsh.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.FirewallPortsMisconfiguredCode, badSsh.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.FirewallPortsMisconfiguredCode, noPanel.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.FirewallPortsMisconfiguredCode, denied.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.FirewallPortsMisconfiguredCode, listed.Error!.Code);
        Assert.Equal(SimulatedAgentErrors.FirewallPortsMisconfiguredCode, listedNoPanel.Error!.Code);
    }

    /// <summary>Once the firewall holds as many rules as it will, allowing another answers limit reached.</summary>
    [Fact]
    public async Task Allowing_a_rule_beyond_the_ceiling_answers_limit_reached()
    {
        var (client, _) = NewClient();
        for (var port = 1; port <= SimulatedAgentLimits.Default.MaxFirewallRules; port++)
        {
            var allowed = await client.AllowPortAsync(
                port,
                null,
                AgentFirewallProtocol.Tcp,
                AnySource,
                SshPorts,
                PanelPort,
                CancellationToken.None);
            Assert.True(allowed.IsSuccess);
        }

        var over = await client.AllowPortAsync(
            SimulatedAgentLimits.Default.MaxFirewallRules + 1,
            null,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.LimitReachedCode, over.Error!.Code);
    }

    /// <summary>Denying removes the rule that matches by its whole value and leaves the others.</summary>
    [Fact]
    public async Task Denying_removes_the_rule_that_matches_by_its_whole_value_and_leaves_the_others()
    {
        var (client, _) = NewClient();
        await client.AllowPortAsync(
            8080,
            8090,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);
        await client.AllowPortAsync(
            8080,
            null,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        var denied = await client.DenyPortAsync(
            8080,
            null,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        Assert.True(denied.IsSuccess);
        var remaining = Assert.Single((await client.ListRulesAsync(SshPorts, PanelPort, CancellationToken.None)).Value);
        Assert.Equal(new AgentFirewallRule(8080, 8090, AgentFirewallProtocol.Tcp, AnySource), remaining);
    }

    /// <summary>Denying a port that is not allowed is a no-op success.</summary>
    [Fact]
    public async Task Denying_a_port_that_is_not_allowed_is_a_no_op_success()
    {
        var (client, _) = NewClient();

        var denied = await client.DenyPortAsync(
            8080,
            null,
            AgentFirewallProtocol.Tcp,
            AnySource,
            SshPorts,
            PanelPort,
            CancellationToken.None);

        Assert.True(denied.IsSuccess);
    }

    /// <summary>A permanent ban is listed with no time left, and an unban removes it.</summary>
    [Fact]
    public async Task A_permanent_ban_is_listed_with_no_time_left_and_an_unban_removes_it()
    {
        var (client, _) = NewClient();
        await client.BanAsync("203.0.113.7", new AgentBanTerm.Permanent(), CancellationToken.None);

        var listed = await client.ListBansAsync(CancellationToken.None);
        var unbanned = await client.UnbanAsync("203.0.113.7", CancellationToken.None);

        Assert.Equal(new AgentFirewallBan("203.0.113.7", null), Assert.Single(listed.Value));
        Assert.True(unbanned.IsSuccess);
        Assert.Empty((await client.ListBansAsync(CancellationToken.None)).Value);
    }

    /// <summary>
    /// A timed ban drops the fraction of a second and lists the whole seconds it has left as the clock moves.
    /// </summary>
    [Fact]
    public async Task A_timed_ban_drops_the_fraction_of_a_second_and_lists_the_whole_seconds_left()
    {
        var (client, clock) = NewClientWithClock();
        await client.BanAsync(
            "203.0.113.7",
            new AgentBanTerm.ExpiresAfter(TimeSpan.FromSeconds(90.7)),
            CancellationToken.None);

        var atStart = await client.ListBansAsync(CancellationToken.None);
        clock.UtcNow = clock.UtcNow.AddSeconds(30);
        var later = await client.ListBansAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(90), Assert.Single(atStart.Value).ExpiresIn);
        Assert.Equal(TimeSpan.FromSeconds(60), Assert.Single(later.Value).ExpiresIn);
    }

    /// <summary>A timed ban that has run out is gone from the listing, and unbanning it answers not found.</summary>
    [Fact]
    public async Task A_timed_ban_that_has_run_out_is_gone_and_unbanning_it_answers_not_found()
    {
        var (client, clock) = NewClientWithClock();
        await client.BanAsync(
            "203.0.113.7",
            new AgentBanTerm.ExpiresAfter(TimeSpan.FromSeconds(10)),
            CancellationToken.None);

        clock.UtcNow = clock.UtcNow.AddSeconds(10);
        var listed = await client.ListBansAsync(CancellationToken.None);
        var unbanned = await client.UnbanAsync("203.0.113.7", CancellationToken.None);

        Assert.Empty(listed.Value);
        Assert.Equal(SimulatedAgentErrors.NotFoundCode, unbanned.Error!.Code);
    }

    /// <summary>Banning an address again replaces its term rather than failing.</summary>
    [Fact]
    public async Task Banning_an_address_again_replaces_its_term()
    {
        var (client, _) = NewClientWithClock();
        await client.BanAsync(
            "203.0.113.7",
            new AgentBanTerm.ExpiresAfter(TimeSpan.FromSeconds(10)),
            CancellationToken.None);

        var again = await client.BanAsync("203.0.113.7", new AgentBanTerm.Permanent(), CancellationToken.None);

        Assert.True(again.IsSuccess);
        Assert.Null(Assert.Single((await client.ListBansAsync(CancellationToken.None)).Value).ExpiresIn);
    }

    /// <summary>
    /// A term under one second, a negative one, one the wire cannot hold, and an address that does not parse are
    /// invalid input.
    /// </summary>
    [Fact]
    public async Task An_unusable_term_or_address_is_invalid_input_and_bans_nothing()
    {
        var (client, _) = NewClient();
        var underOneSecond = TimeSpan.FromMilliseconds(999);
        var negative = TimeSpan.FromSeconds(-5);
        var tooLong = TimeSpan.FromSeconds((double)uint.MaxValue + 1);

        var results = new[]
        {
            await client.BanAsync("203.0.113.7", new AgentBanTerm.ExpiresAfter(underOneSecond), CancellationToken.None),
            await client.BanAsync("203.0.113.7", new AgentBanTerm.ExpiresAfter(negative), CancellationToken.None),
            await client.BanAsync("203.0.113.7", new AgentBanTerm.ExpiresAfter(tooLong), CancellationToken.None),
            await client.BanAsync("not an address", new AgentBanTerm.Permanent(), CancellationToken.None),
            await client.UnbanAsync("not an address", CancellationToken.None),
        };

        Assert.All(results, result => { Assert.Equal(SimulatedAgentErrors.InvalidInputCode, result.Error!.Code); });
        Assert.Empty((await client.ListBansAsync(CancellationToken.None)).Value);
    }

    /// <summary>An IPv4-mapped spelling of an address is the same ban as the plain one.</summary>
    [Fact]
    public async Task An_ipv4_mapped_spelling_of_an_address_is_the_same_ban_as_the_plain_one()
    {
        var (client, _) = NewClient();
        await client.BanAsync("::ffff:203.0.113.7", new AgentBanTerm.Permanent(), CancellationToken.None);

        var listed = await client.ListBansAsync(CancellationToken.None);
        var unbanned = await client.UnbanAsync("203.0.113.7", CancellationToken.None);

        Assert.Equal("203.0.113.7", Assert.Single(listed.Value).Address);
        Assert.True(unbanned.IsSuccess);
    }

    /// <summary>Unbanning an address that was never banned answers not found.</summary>
    [Fact]
    public async Task Unbanning_an_address_that_was_never_banned_answers_not_found()
    {
        var (client, _) = NewClient();

        var unbanned = await client.UnbanAsync("203.0.113.7", CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.NotFoundCode, unbanned.Error!.Code);
    }

    /// <summary>
    /// Banning beyond the ceiling answers limit reached, and banning a held address again still works.
    /// </summary>
    [Fact]
    public async Task Banning_beyond_the_ceiling_answers_limit_reached_but_rebanning_a_held_address_still_works()
    {
        var (client, _) = NewClient();
        for (var index = 0; index < SimulatedAgentLimits.Default.MaxFirewallBans; index++)
        {
            var address = $"10.{(index >> 16) & 255}.{(index >> 8) & 255}.{index & 255}";
            Assert.True(
                (await client.BanAsync(address, new AgentBanTerm.Permanent(), CancellationToken.None)).IsSuccess);
        }

        var over = await client.BanAsync("192.0.2.1", new AgentBanTerm.Permanent(), CancellationToken.None);
        var rebanned = await client.BanAsync("10.0.0.0", new AgentBanTerm.Permanent(), CancellationToken.None);

        Assert.Equal(SimulatedAgentErrors.LimitReachedCode, over.Error!.Code);
        Assert.True(rebanned.IsSuccess);
    }

    /// <summary>Bans are listed ordered by address text so the listing is the same on every call.</summary>
    [Fact]
    public async Task Bans_are_listed_ordered_by_address_text()
    {
        var (client, _) = NewClient();
        await client.BanAsync("203.0.113.9", new AgentBanTerm.Permanent(), CancellationToken.None);
        await client.BanAsync("198.51.100.1", new AgentBanTerm.Permanent(), CancellationToken.None);

        var listed = await client.ListBansAsync(CancellationToken.None);

        Assert.Equal(BannedAddresses, listed.Value.Select(ban => { return ban.Address; }));
    }

    /// <summary>Builds a client over a fresh ledger and a fixed clock.</summary>
    /// <returns>The client and the ledger it reads.</returns>
    private static (SimulatedAgentFirewallClient Client, SimulatedAgentLedger Ledger) NewClient()
    {
        var ledger = new SimulatedAgentLedger(SimulatedAgentLimits.Default);

        return (new SimulatedAgentFirewallClient(ledger, new FixedClock()), ledger);
    }

    /// <summary>Builds a client over a fresh ledger and returns the clock, so a test can move time.</summary>
    /// <returns>The client and the clock it reads.</returns>
    private static (SimulatedAgentFirewallClient Client, FixedClock Clock) NewClientWithClock()
    {
        var clock = new FixedClock();

        return (new SimulatedAgentFirewallClient(new SimulatedAgentLedger(SimulatedAgentLimits.Default), clock), clock);
    }
}
