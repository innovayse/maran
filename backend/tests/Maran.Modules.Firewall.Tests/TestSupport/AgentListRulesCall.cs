namespace Maran.Modules.Firewall.Tests.TestSupport;

/// <summary>One <c>ListRules</c> call the panel made.</summary>
/// <remarks>
/// A read records the host facts too. They are not decoration on a listing: the agent uses them to
/// leave the ruleset's own unconditional accepts out of what it reports, and a listing that showed
/// them would offer an administrator a "deny" button for the rule holding their session open.
/// </remarks>
/// <param name="SshPorts">Every SSH port the panel told the agent about.</param>
/// <param name="PanelPort">The panel port the panel told the agent about.</param>
public sealed record AgentListRulesCall(IReadOnlyList<int> SshPorts, int PanelPort);
