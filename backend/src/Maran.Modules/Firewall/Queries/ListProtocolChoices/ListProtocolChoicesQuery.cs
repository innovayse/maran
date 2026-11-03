namespace Maran.Modules.Firewall.Queries.ListProtocolChoices;

/// <summary>
/// Lists the transport protocols a firewall rule may name, for the rule form's protocol picker:
/// reference data the form selects from, produced by the panel rather than typed or hard-coded by
/// the browser (rules/architecture.md "The backend owns the data, the SPA renders it").
/// </summary>
/// <remarks>
/// The set is the agent contract's own <c>AgentFirewallProtocol</c>, so a protocol the agent learns
/// appears here with the one resx entry that names it. The allow and deny commands keep validating
/// the value against the same enum: the picker is advice and the endpoint is the boundary.
/// </remarks>
public sealed record ListProtocolChoicesQuery;
