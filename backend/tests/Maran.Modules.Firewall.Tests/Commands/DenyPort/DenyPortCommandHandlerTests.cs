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
        Assert.Equal(FirewallAuditActions.FirewallRuleDenied, entry.Action);
        Assert.Equal("tcp/8080 from 0.0.0.0/0", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A deny the agent refuses is journalled as a failure.</summary>
    [Fact]
    public async Task A_deny_the_agent_refuses_is_journalled_as_a_failure()
    {
        var world = new World();
        world.Agent.DenyResult = Result.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.DenyAsync(8080);

        Assert.False(result.IsSuccess);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>A deny for a range sends both bounds, so it removes the rule that exists.</summary>
    [Fact]
    public async Task A_deny_for_a_range_sends_both_bounds()
    {
        // A rule is matched by its whole value, so a deny that forwarded only the lower bound would
        // ask the agent to remove the single port 30000 — which is not installed. The agent would
        // answer NotFound, and the range would stay open while the screen said it was removed.
        var world = new World();

        await world.DenyAsync(30_000, AgentFirewallProtocol.Tcp, "0.0.0.0/0", 30_099);

        var call = Assert.Single(world.Agent.Denies);
        Assert.Equal(30_000, call.Port);
        Assert.Equal(30_099, call.PortTo);
        Assert.Equal("tcp/30000-30099 from 0.0.0.0/0", Assert.Single(world.Audit.Entries).Subject);
    }

    /// <summary>Removing one of two rules for an SSH port, the one that admits the caller, is refused.</summary>
    [Fact]
    public async Task Removing_the_rule_that_admits_the_caller_while_another_rule_remains_is_refused()
    {
        // The agent restores the unconditional accept only when the LAST rule goes, so with rule B
        // left behind the port admits 192.0.2.0/24 and nobody else: the caller is cut off.
        var world = new World();
        world.Agent.RulesResult = Result<IReadOnlyList<AgentFirewallRule>>.Ok(
        [
            new AgentFirewallRule(22, null, AgentFirewallProtocol.Tcp, "198.51.100.1/32"),
            new AgentFirewallRule(22, null, AgentFirewallProtocol.Tcp, "192.0.2.0/24"),
        ]);

        var result = await world.DenyAsync(22, AgentFirewallProtocol.Tcp, "198.51.100.1/32");

        Assert.False(result.IsSuccess);
        Assert.Equal("FirewallRuleLocksOutCaller", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Empty(world.Agent.Denies);
        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(FirewallAuditActions.FirewallRuleDenied, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.Single(world.Log.Messages);
    }

    /// <summary>The inverse control: removing the last rule for the port is allowed.</summary>
    [Fact]
    public async Task Removing_the_last_rule_for_an_ssh_port_is_sent_to_the_agent()
    {
        var world = new World();
        world.Agent.RulesResult = Result<IReadOnlyList<AgentFirewallRule>>.Ok(
            [new AgentFirewallRule(22, null, AgentFirewallProtocol.Tcp, "198.51.100.1/32")]);

        var result = await world.DenyAsync(22, AgentFirewallProtocol.Tcp, "198.51.100.1/32");

        Assert.True(result.IsSuccess);
        Assert.Single(world.Agent.Denies);
    }

    /// <summary>The inverse control: removing a rule that does not admit the caller, leaving one that does, is allowed.</summary>
    [Fact]
    public async Task Removing_a_rule_while_another_rule_still_admits_the_caller_is_sent_to_the_agent()
    {
        var world = new World();
        world.Agent.RulesResult = Result<IReadOnlyList<AgentFirewallRule>>.Ok(
        [
            new AgentFirewallRule(22, null, AgentFirewallProtocol.Tcp, "198.51.100.0/24"),
            new AgentFirewallRule(22, null, AgentFirewallProtocol.Tcp, "192.0.2.0/24"),
        ]);

        var result = await world.DenyAsync(22, AgentFirewallProtocol.Tcp, "192.0.2.0/24");

        Assert.True(result.IsSuccess);
        Assert.Single(world.Agent.Denies);
    }

    /// <summary>A rule listing that fails stops the removal: the handler cannot tell, and does not guess.</summary>
    [Fact]
    public async Task A_rule_listing_that_fails_stops_the_removal()
    {
        var world = new World();
        world.Agent.RulesResult = Result<IReadOnlyList<AgentFirewallRule>>.Fail(
            Error.Of("AgentSystemFailure", ErrorType.Failure));

        var result = await world.DenyAsync(22, AgentFirewallProtocol.Tcp, "198.51.100.1/32");

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
        Assert.Empty(world.Agent.Denies);
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

        /// <summary>The operator log double.</summary>
        public RecordingLogger<DenyPortCommandHandler> Log { get; } = new();

        /// <summary>Builds a handler over a host with two SSH ports and nginx on 8443.</summary>
        public World()
        {
            _handler = new DenyPortCommandHandler(
                Agent,
                FirewallTestContext.Options(),
                new FirewallAuditJournal(Audit, new FakeCurrentUser()),
                Log);
        }

        /// <summary>Runs the handler once.</summary>
        /// <param name="port">The port to close.</param>
        /// <param name="protocol">The protocol the rule applies to.</param>
        /// <param name="sourceCidr">The source range the allow was scoped to.</param>
        /// <param name="portTo">The range's upper bound, or null for a single port.</param>
        public async Task<Result> DenyAsync(
            int port,
            AgentFirewallProtocol protocol = AgentFirewallProtocol.Tcp,
            string sourceCidr = "0.0.0.0/0",
            int? portTo = null)
        {
            return await _handler.HandleAsync(
                new DenyPortCommand(port, protocol, sourceCidr, portTo, "198.51.100.1", "curl"),
                CancellationToken.None);
        }
    }
}
