using Maran.Agent.Client.Services.FirewallService;
using Maran.Modules.Firewall.Commands.DenyPort;
using Maran.Modules.Firewall.Services;
using Maran.Modules.Firewall.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Firewall.Tests.Commands.DenyPort;

/// <summary>What a deny sends, and why it carries the same host facts an allow does.</summary>
public sealed class DenyPortCommandHandlerTests
{
    /// <summary>A deny carries the host facts too because it re renders the whole ruleset.</summary>
    [Fact]
    public async Task A_deny_carries_the_host_facts_too_because_it_re_renders_the_whole_ruleset()
    {
        // Closing a port is not a smaller operation than opening one: the agent renders the entire
        // ruleset either way, so a deny can lock the operator out just as thoroughly.
        var world = new World();

        await world.DenyAsync(8080);

        var call = Assert.Single(world.Agent.Denies);
        Assert.Equal([22, 2222], call.SshPorts);
        Assert.Equal(8443, call.PanelPort);
    }

    /// <summary>A deny names the source range the allow was scoped to.</summary>
    [Fact]
    public async Task A_deny_names_the_source_range_the_allow_was_scoped_to()
    {
        // A port allowed from one office and from a monitoring probe is two rules, and a deny that
        // ignored the range would remove whichever the firewall happened to list first.
        var world = new World();

        await world.DenyAsync(8080, AgentFirewallProtocol.Tcp, "198.51.100.0/24");

        Assert.Equal("198.51.100.0/24", Assert.Single(world.Agent.Denies).SourceCidr);
    }

    /// <summary>A deny is journalled with the rule it closed spelled as the allow spelled it.</summary>
    [Fact]
    public async Task A_deny_is_journalled_with_the_rule_it_closed_spelled_as_the_allow_spelled_it()
    {
        var world = new World();

        await world.DenyAsync(8080, AgentFirewallProtocol.Tcp, "0.0.0.0/0");

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.FirewallRuleDenied, entry.Action);
        Assert.Equal("tcp/8080 from 0.0.0.0/0", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A deny the agent refuses is journalled as a failure.</summary>
    [Fact]
    public async Task A_deny_the_agent_refuses_is_journalled_as_a_failure()
    {
        var world = new World();
        world.Agent.DenyResult = Result<bool>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.DenyAsync(8080);

        Assert.False(result.IsSuccess);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>The agent double, the journal and the handler under test.</summary>
    private sealed class World
    {
        /// <summary>The handler under test.</summary>
        private readonly DenyPortCommandHandler _handler;

        /// <summary>The agent double, which records what the panel decided to send.</summary>
        public RecordingAgentFirewallClient Agent { get; } = new();

        /// <summary>The journal double.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>Builds a handler over a host with two SSH ports and nginx on 8443.</summary>
        public World()
        {
            _handler = new DenyPortCommandHandler(
                Agent,
                FirewallTestContext.Options(),
                new FirewallAuditJournal(Audit, new FakeCurrentUser()));
        }

        /// <summary>Runs the handler once.</summary>
        /// <param name="port">The port to close.</param>
        /// <param name="protocol">The protocol the rule applies to.</param>
        /// <param name="sourceCidr">The source range the allow was scoped to.</param>
        public async Task<Result<bool>> DenyAsync(
            int port,
            AgentFirewallProtocol protocol = AgentFirewallProtocol.Tcp,
            string sourceCidr = "0.0.0.0/0")
        {
            return await _handler.HandleAsync(
                new DenyPortCommand(port, protocol, sourceCidr, "198.51.100.1", "curl"),
                CancellationToken.None);
        }
    }
}
