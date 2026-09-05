using Maran.Agent.Client.Services.FirewallService;

namespace Maran.Modules.Firewall.Tests.TestSupport;

/// <summary>One <c>AllowPort</c> or <c>DenyPort</c> call the panel made.</summary>
/// <remarks>
/// It carries the host facts as well as the rule, because those are what the tests are really
/// about: the agent re-renders the whole ruleset from them under a drop policy, so a call that
/// arrived without them is a locked-out server.
/// </remarks>
/// <param name="Port">The port the rule names.</param>
/// <param name="Protocol">The transport protocol it applies to.</param>
/// <param name="SourceCidr">The source range it is scoped to.</param>
/// <param name="SshPorts">Every SSH port the panel told the agent about.</param>
/// <param name="PanelPort">The panel port the panel told the agent about.</param>
public sealed record AgentRuleCall(
    int Port,
    AgentFirewallProtocol Protocol,
    string SourceCidr,
    IReadOnlyList<int> SshPorts,
    int PanelPort);
