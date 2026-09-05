using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.FirewallService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Firewall.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentFirewallClient"/> double that records every call and replays a scripted
/// outcome. Deliberately dumb: it asserts nothing itself, so the tests say what they mean.
/// </summary>
/// <remarks>
/// The real client is not merely a transport — it refuses an empty SSH port list and a sub-second
/// ban before sending anything — but those refusals have their own tests in
/// <c>Maran.Agent.Client.Tests</c>. What the module's tests need from this seam is the OPPOSITE
/// question: what did the panel decide to send.
/// </remarks>
public sealed class RecordingAgentFirewallClient : IAgentFirewallClient
{
    /// <summary>Every ban the panel asked for, in order.</summary>
    public List<AgentBanCall> Bans { get; } = [];

    /// <summary>Every address the panel asked to unban, in order.</summary>
    public List<string> Unbans { get; } = [];

    /// <summary>Every allow the panel asked for, in order.</summary>
    public List<AgentRuleCall> Allows { get; } = [];

    /// <summary>Every deny the panel asked for, in order.</summary>
    public List<AgentRuleCall> Denies { get; } = [];

    /// <summary>Every rule listing the panel asked for, in order.</summary>
    public List<AgentListRulesCall> RuleListings { get; } = [];

    /// <summary>How many times the panel asked the agent for the ban listing.</summary>
    /// <remarks>
    /// Counted so a test can assert it stays at zero. The kernel's ban listing carries no reason,
    /// and a screen built from it would be missing the one column it exists for.
    /// </remarks>
    public int BanListings { get; private set; }

    /// <summary>What <see cref="BanAsync"/> answers; success unless a test says otherwise.</summary>
    public Result<bool> BanResult { get; set; } = Result<bool>.Ok(true);

    /// <summary>What <see cref="UnbanAsync"/> answers.</summary>
    public Result<bool> UnbanResult { get; set; } = Result<bool>.Ok(true);

    /// <summary>What <see cref="AllowPortAsync"/> answers.</summary>
    public Result<bool> AllowResult { get; set; } = Result<bool>.Ok(true);

    /// <summary>What <see cref="DenyPortAsync"/> answers.</summary>
    public Result<bool> DenyResult { get; set; } = Result<bool>.Ok(true);

    /// <summary>What <see cref="ListRulesAsync"/> answers.</summary>
    public Result<IReadOnlyList<AgentFirewallRule>> RulesResult { get; set; } =
        Result<IReadOnlyList<AgentFirewallRule>>.Ok([]);

    /// <summary>Addresses this client refuses to ban, whatever <see cref="BanResult"/> says.</summary>
    /// <remarks>
    /// So a test can drive the one case a whole-pass result cannot express: the reconciler meeting
    /// one address the agent will not take while every other ban goes in perfectly.
    /// </remarks>
    public HashSet<string> AddressesThatFailToBan { get; } = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentFirewallRule>>> ListRulesAsync(
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        RuleListings.Add(new AgentListRulesCall([.. sshPorts], panelPort));
        return Task.FromResult(RulesResult);
    }

    /// <inheritdoc />
    public Task<Result<bool>> AllowPortAsync(
        int port,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        Allows.Add(new AgentRuleCall(port, protocol, sourceCidr, [.. sshPorts], panelPort));
        return Task.FromResult(AllowResult);
    }

    /// <inheritdoc />
    public Task<Result<bool>> DenyPortAsync(
        int port,
        AgentFirewallProtocol protocol,
        string sourceCidr,
        IReadOnlyList<int> sshPorts,
        int panelPort,
        CancellationToken cancellationToken)
    {
        Denies.Add(new AgentRuleCall(port, protocol, sourceCidr, [.. sshPorts], panelPort));
        return Task.FromResult(DenyResult);
    }

    /// <inheritdoc />
    public Task<Result<bool>> BanAsync(string address, TimeSpan? ttl, CancellationToken cancellationToken)
    {
        Bans.Add(new AgentBanCall(address, ttl));

        return Task.FromResult(
            AddressesThatFailToBan.Contains(address)
                ? Result<bool>.Fail(Error.Of("AgentInvalidInput", ErrorType.Validation))
                : BanResult);
    }

    /// <inheritdoc />
    public Task<Result<bool>> UnbanAsync(string address, CancellationToken cancellationToken)
    {
        Unbans.Add(address);
        return Task.FromResult(UnbanResult);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentFirewallBan>>> ListBansAsync(CancellationToken cancellationToken)
    {
        BanListings++;
        return Task.FromResult(Result<IReadOnlyList<AgentFirewallBan>>.Ok([]));
    }
}
