namespace Maran.Agent.Client.Services.FirewallService;

/// <summary>One port rule currently installed in the host's firewall.</summary>
/// <param name="Port">The port the rule names, 1-65535.</param>
/// <param name="Protocol">The transport protocol the rule applies to.</param>
/// <param name="SourceCidr">
/// The source range the rule is scoped to, e.g. <c>0.0.0.0/0</c> for any source.
/// </param>
/// <remarks>
/// A listing reports the rules somebody ASKED for, and not every accept the host is running: the
/// rendered policy also carries unconditional accepts for the host's SSH ports and the panel's own
/// port, and the agent leaves those out because they are byte-identical to an operator's own
/// any-source allow. Telling them apart is why the listing call is given those ports — without
/// them the panel would show accepts nobody created, and an administrator would then try to deny
/// one and lock themselves out.
///
/// The source is the canonical spelling the firewall is actually running, not an echo of whatever
/// a caller once sent, so it is the value to send back to deny the rule again.
/// </remarks>
public sealed record AgentFirewallRule(int Port, AgentFirewallProtocol Protocol, string SourceCidr);
