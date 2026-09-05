using Maran.Agent.Client.Services.FirewallService;

namespace Maran.Modules.Firewall.Controllers.Requests;

/// <summary>The query string of <c>DELETE /api/v1/firewall/rules</c>.</summary>
/// <remarks>
/// A rule has no identifier — it IS its port, protocol and source range — so the three of them are
/// what a delete has to name. They travel in the query string rather than in a body because a
/// request body on DELETE is ignored by enough intermediaries that a rule would sometimes silently
/// fail to be removed.
/// </remarks>
/// <param name="Port">The port to stop allowing, 1-65535.</param>
/// <param name="Protocol">The transport protocol the rule applies to.</param>
/// <param name="SourceCidr">The source range the original allow was scoped to.</param>
public sealed record DenyPortRequest(int Port, AgentFirewallProtocol Protocol, string SourceCidr);
