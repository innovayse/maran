using Maran.Agent.Client.Services.FirewallService;

namespace Maran.Host.Demo.World;

/// <summary>One firewall port rule of the demo world.</summary>
/// <param name="Port">The first port.</param>
/// <param name="Protocol">The protocol.</param>
/// <param name="SourceCidr">The source range the rule applies to.</param>
/// <param name="Allow">True to allow, false to deny.</param>
public sealed record DemoPortRuleDefinition(int Port, AgentFirewallProtocol Protocol, string SourceCidr, bool Allow);
