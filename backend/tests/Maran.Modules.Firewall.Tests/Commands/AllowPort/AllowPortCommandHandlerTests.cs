using Maran.Agent.Client.Services.FirewallService;
using Maran.Modules.Firewall.Commands.AllowPort;
using Maran.Modules.Firewall.Services;
using Maran.Modules.Firewall.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Firewall.Tests.Commands.AllowPort;

/// <summary>What an allow sends to the agent, which is what decides whether the server stays reachable.</summary>
public sealed class AllowPortCommandHandlerTests
{
    /// <summary>An allow carries every ssh port the host listens on.</summary>
    [Fact]
    public async Task An_allow_carries_every_ssh_port_the_host_listens_on()
    {
        // The agent re-renders the WHOLE ruleset on this call under a drop policy, so these ports
        // are the only reason the operator's own session survives an unrelated rule change. sshd
        // listens on every Port directive it is given, so allowing all but one costs whoever is
        // connected on that one.
        var world = new World(sshPorts: "22,2200,2222");

        await world.AllowAsync(8080);

        Assert.Equal([22, 2200, 2222], Assert.Single(world.Agent.Allows).SshPorts);
    }

    /// <summary>An allow carries the panels public port as configured.</summary>
    [Fact]
    public async Task An_allow_carries_the_panels_public_port_as_configured()
    {
        // 8443 is nginx's public vhost, and nothing else is a candidate: on a server the api has
        // no port at all, listening on a unix socket, and 5080 is a development-only address.
        // Rendering an accept for that one under a drop policy leaves the panel reachable until the
        // first rule change and dead afterwards.
        var world = new World(panelPort: 8443);

        await world.AllowAsync(8080);

        Assert.Equal(8443, Assert.Single(world.Agent.Allows).PanelPort);
    }

    /// <summary>An allow sends the rule the caller asked for.</summary>
    [Fact]
    public async Task An_allow_sends_the_rule_the_caller_asked_for()
    {
        var world = new World();

        await world.AllowAsync(8080, AgentFirewallProtocol.Udp, "198.51.100.0/24");

        var call = Assert.Single(world.Agent.Allows);
        Assert.Equal(8080, call.Port);
        Assert.Equal(AgentFirewallProtocol.Udp, call.Protocol);
        Assert.Equal("198.51.100.0/24", call.SourceCidr);
    }

    /// <summary>An allow is journalled with the rule it opened.</summary>
    [Fact]
    public async Task An_allow_is_journalled_with_the_rule_it_opened()
    {
        // A rule has no identifier, so the journal's subject has to BE the rule — and spelled the
        // same way the matching deny will spell it, or the two entries bracketing a port's life
        // cannot be found by one search.
        var world = new World();

        await world.AllowAsync(8080, AgentFirewallProtocol.Tcp, "0.0.0.0/0");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(FirewallAuditActions.FirewallRuleAllowed, entry.Action);
        Assert.Equal("tcp/8080 from 0.0.0.0/0", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An allow over a range sends both bounds and journals both.</summary>
    [Fact]
    public async Task An_allow_over_a_range_sends_both_bounds_and_journals_both()
    {
        // Both halves by VALUE. "an allow was sent" and "an entry was written" both pass against a
        // range that silently collapsed to its first port, which is a hundred ports an
        // administrator believes are open and one that is.
        var world = new World();

        await world.AllowAsync(30_000, AgentFirewallProtocol.Tcp, "0.0.0.0/0", 30_099);

        var call = Assert.Single(world.Agent.Allows);
        Assert.Equal(30_000, call.Port);
        Assert.Equal(30_099, call.PortTo);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal("tcp/30000-30099 from 0.0.0.0/0", entry.Subject);
    }

    /// <summary>An allow for a single port sends no upper bound at all.</summary>
    [Fact]
    public async Task An_allow_for_a_single_port_sends_no_upper_bound_at_all()
    {
        // The compatibility claim, at the panel's end: a rule that names one port must reach the
        // agent exactly as it did before ranges existed — and be journalled under the same subject,
        // so the entries bracketing an old rule's life are still found by one search.
        var world = new World();

        await world.AllowAsync(8080);

        Assert.Null(Assert.Single(world.Agent.Allows).PortTo);
        Assert.Equal("tcp/8080 from 0.0.0.0/0", Assert.Single(world.Audit.Entries).Subject);
    }

    /// <summary>An allow the agent refuses is journalled as a failure and returns its code.</summary>
    [Fact]
    public async Task An_allow_the_agent_refuses_is_journalled_as_a_failure_and_returns_its_code()
    {
        var world = new World();
        world.Agent.AllowResult = Result.Fail(Error.Of("AgentAlreadyExists", ErrorType.Conflict));

        var result = await world.AllowAsync(8080);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentAlreadyExists", result.Error!.Code);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>A misconfigured host is reported as the operators problem and not the callers.</summary>
    [Fact]
    public async Task A_misconfigured_host_is_reported_as_the_operators_problem_and_not_the_callers()
    {
        // The agent client answers AgentFirewallPortsMisconfigured — not the AgentInvalidInput it
        // uses for a bad rule port — because the two failures have opposite audiences. The handler
        // must pass it through unchanged; telling an API caller they submitted bad details would
        // send them to check a request that was perfectly good.
        var world = new World();
        world.Agent.AllowResult = Result.Fail(Error.Of("AgentFirewallPortsMisconfigured", ErrorType.Failure));

        var result = await world.AllowAsync(8080);

        Assert.Equal("AgentFirewallPortsMisconfigured", result.Error!.Code);
    }

    /// <summary>An allow that would close an SSH port to the caller is refused and sends nothing.</summary>
    [Fact]
    public async Task An_allow_that_would_cut_the_caller_off_an_ssh_port_is_refused_and_sends_nothing()
    {
        var world = new World(sshPorts: "22,2222");

        var result = await world.AllowAsync(2222, sourceCidr: "203.0.113.0/24");

        Assert.False(result.IsSuccess);
        Assert.Equal("FirewallRuleLocksOutCaller", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(world.Agent.Allows);
    }

    /// <summary>The refusal is journalled as a failed allow carrying the rule that was stopped.</summary>
    [Fact]
    public async Task A_refused_allow_is_journalled_as_a_failure_naming_the_rule()
    {
        var world = new World(sshPorts: "22,2222");

        await world.AllowAsync(2222, sourceCidr: "203.0.113.0/24");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(FirewallAuditActions.FirewallRuleAllowed, entry.Action);
        Assert.Equal("tcp/2222 from 203.0.113.0/24", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>The operator log names the SSH port the refused allow would have closed.</summary>
    [Fact]
    public async Task A_refused_allow_logs_the_ssh_port_it_would_have_closed()
    {
        var world = new World(sshPorts: "22,2222");

        await world.AllowAsync(2222, sourceCidr: "203.0.113.0/24");

        var line = Assert.Single(world.Log.Messages);
        Assert.Contains("2222", line);
        Assert.Contains("198.51.100.1", line);
    }

    /// <summary>The same allow, scoped to a range that includes the caller, is installed.</summary>
    [Fact]
    public async Task The_same_allow_with_a_range_that_includes_the_caller_is_installed()
    {
        // The inverse control: a guard that refused every SSH-port allow would pass the test above.
        var world = new World(sshPorts: "22,2222");

        var result = await world.AllowAsync(2222, sourceCidr: "198.51.100.0/24");

        Assert.True(result.IsSuccess);
        Assert.Equal("198.51.100.0/24", Assert.Single(world.Agent.Allows).SourceCidr);
    }

    /// <summary>The guard reads the SSH ports from the host facts and not from the request.</summary>
    [Fact]
    public async Task The_guard_uses_the_ssh_ports_the_host_was_configured_with()
    {
        // Port 2222 is only an SSH port when the host says so; on a host whose sshd listens on 22
        // alone, a narrowed rule for 2222 is an ordinary one.
        var world = new World(sshPorts: "22");

        var result = await world.AllowAsync(2222, sourceCidr: "203.0.113.0/24");

        Assert.True(result.IsSuccess);
        Assert.Single(world.Agent.Allows);
    }

    /// <summary>The guard reads the rules the host runs, sending it the same host facts a listing needs.</summary>
    [Fact]
    public async Task The_guard_reads_the_rules_the_host_is_running_with_the_host_facts()
    {
        var world = new World(sshPorts: "22,2222", panelPort: 8443);

        await world.AllowAsync(8080);

        var listing = Assert.Single(world.Agent.RuleListings);
        Assert.Equal([22, 2222], listing.SshPorts);
        Assert.Equal(8443, listing.PanelPort);
    }

    /// <summary>A rule that already exists for the SSH port makes a further narrowed allow harmless.</summary>
    [Fact]
    public async Task A_narrowed_allow_next_to_an_existing_rule_for_the_ssh_port_is_installed()
    {
        var world = new World(sshPorts: "22,2222");
        world.Agent.RulesResult = Result<IReadOnlyList<AgentFirewallRule>>.Ok(
            [new AgentFirewallRule(2222, null, AgentFirewallProtocol.Tcp, "198.51.100.0/24")]);

        var result = await world.AllowAsync(2222, sourceCidr: "203.0.113.0/24");

        Assert.True(result.IsSuccess);
        Assert.Single(world.Agent.Allows);
    }

    /// <summary>A caller the panel cannot attribute is refused a restricted SSH rule.</summary>
    [Fact]
    public async Task A_caller_without_an_address_is_refused_a_restricted_ssh_rule()
    {
        var world = new World(sshPorts: "22,2222");

        var result = await world.AllowAsync(22, sourceCidr: "203.0.113.0/24", caller: "unknown");

        Assert.Equal("FirewallRuleLocksOutCaller", result.Error!.Code);
        Assert.Empty(world.Agent.Allows);
    }

    /// <summary>When the host's rules cannot be read the allow is not sent, and the failure is journalled.</summary>
    [Fact]
    public async Task An_allow_is_not_sent_when_the_hosts_rules_cannot_be_read()
    {
        // Without the listing the handler cannot tell whether the rule locks anyone out, and it
        // does not guess: the listing's own failure is the answer.
        var world = new World();
        world.Agent.RulesResult = Result<IReadOnlyList<AgentFirewallRule>>.Fail(
            Error.Of("AgentUnavailable", ErrorType.Unavailable));

        var result = await world.AllowAsync(8080);

        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Empty(world.Agent.Allows);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>The agent double, the journal and the handler under test.</summary>
    private sealed class World
    {
        /// <summary>The handler under test.</summary>
        private readonly AllowPortCommandHandler _handler;

        /// <summary>The agent double, which records what the panel decided to send.</summary>
        public RecordingAgentFirewallClient Agent { get; } = new();

        /// <summary>The journal double.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>The operator log double.</summary>
        public RecordingLogger<AllowPortCommandHandler> Log { get; } = new();

        /// <summary>Builds a handler over the host facts a panel was configured with.</summary>
        /// <param name="sshPorts">The raw value of <c>Firewall__SshPorts</c>.</param>
        /// <param name="panelPort">The panel's public port.</param>
        public World(string sshPorts = "22,2222", int panelPort = 8443)
        {
            _handler = new AllowPortCommandHandler(
                Agent,
                FirewallTestContext.Options(sshPorts, panelPort),
                new FirewallAuditJournal(Audit, new FakeCurrentUser()),
                Log);
        }

        /// <summary>Runs the handler once.</summary>
        /// <param name="port">The port to allow.</param>
        /// <param name="protocol">The protocol the rule applies to.</param>
        /// <param name="sourceCidr">The source range to allow from.</param>
        /// <param name="portTo">The range's upper bound, or null for a single port.</param>
        /// <param name="caller">The address the request arrived from.</param>
        public async Task<Result> AllowAsync(
            int port,
            AgentFirewallProtocol protocol = AgentFirewallProtocol.Tcp,
            string sourceCidr = "0.0.0.0/0",
            int? portTo = null,
            string caller = "198.51.100.1")
        {
            return await _handler.HandleAsync(
                new AllowPortCommand(port, protocol, sourceCidr, portTo, caller, "curl"),
                CancellationToken.None);
        }
    }
}
