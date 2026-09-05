using Maran.Agent.Client.Services.FirewallService;

namespace Maran.Modules.Firewall.Common;

/// <summary>Outward view of one port rule the host's firewall is running.</summary>
/// <remarks>
/// A panel-side shape rather than the agent client's <see cref="AgentFirewallRule"/> passed
/// straight through, for the reason every module has its own DTOs: the agent's type is the shape of
/// a transport, and a screen binding to it would break the day the transport grew a field.
///
/// The listing reports the rules somebody ASKED for. The unconditional accepts the agent renders
/// for the host's SSH ports and the panel's own port are not among them — which is why the query
/// has to tell the agent what those ports are. Showing them would offer an administrator a "deny"
/// button for the rule holding their session open.
/// </remarks>
/// <param name="Port">The port the rule names.</param>
/// <param name="Protocol">The transport protocol it applies to.</param>
/// <param name="SourceCidr">
/// The source range it is scoped to, as the firewall is actually running it — the value to send
/// back to remove the rule again.
/// </param>
public sealed record FirewallRuleDto(int Port, AgentFirewallProtocol Protocol, string SourceCidr);
