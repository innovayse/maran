using Maran.Agent.Client.Services.FirewallService;

namespace Maran.Modules.Firewall.Controllers.Requests;

/// <summary>The body of <c>POST /api/v1/firewall/rules</c>.</summary>
/// <remarks>
/// A separate type from the command: the command carries the caller's address and user agent, which
/// are read from the connection and must never be settable by the request that is being audited.
///
/// It has no SSH port and no panel port, and none may be added. Those are host facts the module
/// reads from its own configuration; a request able to state them could state the wrong ones, and
/// the agent renders the entire ruleset from what it is told.
/// </remarks>
/// <param name="Port">The port to allow, 1-65535.</param>
/// <param name="Protocol">The transport protocol the rule applies to.</param>
/// <param name="SourceCidr">The source range to allow from; <c>0.0.0.0/0</c> allows any source.</param>
public sealed record AllowPortRequest(int Port, AgentFirewallProtocol Protocol, string SourceCidr);
