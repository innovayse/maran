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
        Assert.Equal(AuditActions.FirewallRuleAllowed, entry.Action);
        Assert.Equal("tcp/8080 from 0.0.0.0/0", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An allow the agent refuses is journalled as a failure and returns its code.</summary>
    [Fact]
    public async Task An_allow_the_agent_refuses_is_journalled_as_a_failure_and_returns_its_code()
    {
        var world = new World();
        world.Agent.AllowResult = Result<bool>.Fail(Error.Of("AgentAlreadyExists", ErrorType.Conflict));

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
        world.Agent.AllowResult = Result<bool>.Fail(Error.Of("AgentFirewallPortsMisconfigured", ErrorType.Failure));

        var result = await world.AllowAsync(8080);

        Assert.Equal("AgentFirewallPortsMisconfigured", result.Error!.Code);
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

        /// <summary>Builds a handler over the host facts a panel was configured with.</summary>
        /// <param name="sshPorts">The raw value of <c>Firewall__SshPorts</c>.</param>
        /// <param name="panelPort">The panel's public port.</param>
        public World(string sshPorts = "22,2222", int panelPort = 8443)
        {
            _handler = new AllowPortCommandHandler(
                Agent,
                FirewallTestContext.Options(sshPorts, panelPort),
                new FirewallAuditJournal(Audit, new FakeCurrentUser()));
        }

        /// <summary>Runs the handler once.</summary>
        /// <param name="port">The port to allow.</param>
        /// <param name="protocol">The protocol the rule applies to.</param>
        /// <param name="sourceCidr">The source range to allow from.</param>
        public async Task<Result<bool>> AllowAsync(
            int port,
            AgentFirewallProtocol protocol = AgentFirewallProtocol.Tcp,
            string sourceCidr = "0.0.0.0/0")
        {
            return await _handler.HandleAsync(
                new AllowPortCommand(port, protocol, sourceCidr, "198.51.100.1", "curl"),
                CancellationToken.None);
        }
    }
}
