using Maran.Agent.Client.Services.FirewallService;

namespace Maran.Modules.Firewall.Commands.AllowPort;

/// <summary>
/// Opens a port on the host firewall, optionally scoped to one source range.
/// </summary>
/// <remarks>
/// The command carries no SSH port and no panel port. Those are host facts read from
/// <c>FirewallOptions</c> by the handler, never taken from the request: a caller able to name them
/// could name the wrong ones, and the agent renders the whole ruleset from what it is told.
/// </remarks>
/// <param name="Port">The port to allow, 1-65535.</param>
/// <param name="Protocol">The transport protocol the rule applies to.</param>
/// <param name="SourceCidr">The source range to allow from; <c>0.0.0.0/0</c> allows any source.</param>
/// <param name="IpAddress">The caller's address, recorded in the audit journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the audit journal.</param>
public sealed record AllowPortCommand(
    int Port,
    AgentFirewallProtocol Protocol,
    string SourceCidr,
    string IpAddress,
    string UserAgent);
