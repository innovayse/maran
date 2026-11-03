using Maran.Agent.Client.Services.FirewallService;

namespace Maran.Modules.Firewall.Domain.ValueObjects;

/// <summary>
/// One rule a preset installs: the same three values every firewall rule IS (a port or range, a
/// protocol and a source range), in the shape the allow command takes them.
/// </summary>
/// <param name="Port">The port the rule names, or the lower bound of a range.</param>
/// <param name="PortTo">The inclusive upper bound of a range, or null for a single port.</param>
/// <param name="Protocol">The transport protocol the rule applies to.</param>
/// <param name="SourceCidr">The source range the rule admits.</param>
public sealed record FirewallPresetRule(int Port, int? PortTo, AgentFirewallProtocol Protocol, string SourceCidr);
