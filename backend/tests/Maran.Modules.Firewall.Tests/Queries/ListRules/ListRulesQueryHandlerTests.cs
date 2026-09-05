using Maran.Agent.Client.Services.FirewallService;
using Maran.Modules.Firewall.Queries.ListRules;
using Maran.Modules.Firewall.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Firewall.Tests.Queries.ListRules;

/// <summary>What the rule listing asks the agent for, and what it hands back.</summary>
public sealed class ListRulesQueryHandlerTests
{
    /// <summary>A listing carries the host facts so the rulesets own accepts can be left out.</summary>
    [Fact]
    public async Task A_listing_carries_the_host_facts_so_the_rulesets_own_accepts_can_be_left_out()
    {
        // A read, carrying the same two ports every mutation does. The unconditional accepts the
        // agent renders for the SSH ports and the panel port are byte-identical to an operator's own
        // any-source TCP allow, so without these the listing reports accepts nobody created — and an
        // administrator then tries to deny one.
        var agent = new RecordingAgentFirewallClient();
        var handler = new ListRulesQueryHandler(agent, FirewallTestContext.Options("22,2200,2222", 8443));

        await handler.HandleAsync(new ListRulesQuery(), CancellationToken.None);

        var call = Assert.Single(agent.RuleListings);
        Assert.Equal([22, 2200, 2222], call.SshPorts);
        Assert.Equal(8443, call.PanelPort);
    }

    /// <summary>A listing hands back what the firewall is actually running.</summary>
    [Fact]
    public async Task A_listing_hands_back_what_the_firewall_is_actually_running()
    {
        var agent = new RecordingAgentFirewallClient
        {
            RulesResult = Result<IReadOnlyList<AgentFirewallRule>>.Ok(
                [new AgentFirewallRule(8080, AgentFirewallProtocol.Tcp, "0.0.0.0/0")]),
        };
        var handler = new ListRulesQueryHandler(agent, FirewallTestContext.Options());

        var result = await handler.HandleAsync(new ListRulesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rule = Assert.Single(result.Value);
        Assert.Equal(8080, rule.Port);
        Assert.Equal(AgentFirewallProtocol.Tcp, rule.Protocol);
        Assert.Equal("0.0.0.0/0", rule.SourceCidr);
    }

    /// <summary>An agent that cannot read the ruleset is reported rather than shown as an empty firewall.</summary>
    [Fact]
    public async Task An_agent_that_cannot_read_the_ruleset_is_reported_rather_than_shown_as_an_empty_firewall()
    {
        // An empty list would tell an administrator no ports are open, which is the one wrong answer
        // that reads as reassuring.
        var agent = new RecordingAgentFirewallClient
        {
            RulesResult = Result<IReadOnlyList<AgentFirewallRule>>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        var handler = new ListRulesQueryHandler(agent, FirewallTestContext.Options());

        var result = await handler.HandleAsync(new ListRulesQuery(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentSystemFailure", result.Error!.Code);
    }
}
