using Maran.Agent.Client.Services.FirewallService;

namespace Maran.Modules.Firewall.Common;

/// <summary>One transport protocol the rule form can be offered, as its protocol picker lists it.</summary>
/// <param name="Protocol">
/// The protocol, which is what the form sends back in a rule. It serializes as the camelCase member
/// name, the same spelling a rule uses.
/// </param>
/// <param name="ProtocolName">
/// The protocol as an operator reads it, resolved for the request's culture; the member name itself
/// for a protocol this build carries no entry for.
/// </param>
public sealed record FirewallProtocolChoiceDto(AgentFirewallProtocol Protocol, string ProtocolName);
