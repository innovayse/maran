using Maran.Agent.Client.Services.FirewallService;

namespace Maran.Modules.Firewall.Tests.TestSupport;

/// <summary>One <c>BanAddress</c> call the panel made, exactly as the agent would have received it.</summary>
/// <remarks>
/// The address is recorded as the STRING that went over the wire rather than as a parsed value,
/// because the whole question these tests ask about it is which spelling arrived: the agent refuses
/// <c>::ffff:203.0.113.7</c>, and a parsed <c>IPAddress</c> would have thrown that distinction away.
/// </remarks>
/// <param name="Address">The address as it was sent.</param>
/// <param name="Term">How long the ban was asked to last: permanent, or a fixed duration.</param>
public sealed record AgentBanCall(string Address, AgentBanTerm Term);
