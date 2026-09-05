using Maran.Agent.Client.Services.FirewallService;

namespace Maran.Modules.Firewall.Commands.DenyPort;

/// <summary>
/// Removes an allow from the host firewall, matching the source range the allow was scoped to.
/// </summary>
/// <remarks>
/// The source range is part of what identifies the rule, not decoration: a port allowed from one
/// office and from a monitoring probe is two rules, and a deny that ignored the range would remove
/// whichever the firewall happened to list first.
/// </remarks>
/// <param name="Port">The port to stop allowing, 1-65535.</param>
/// <param name="Protocol">The transport protocol the rule applies to.</param>
/// <param name="SourceCidr">The source range the original allow was scoped to.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record DenyPortCommand(
    int Port,
    AgentFirewallProtocol Protocol,
    string SourceCidr,
    string IpAddress,
    string UserAgent);
