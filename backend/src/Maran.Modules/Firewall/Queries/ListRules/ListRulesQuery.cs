namespace Maran.Modules.Firewall.Queries.ListRules;

/// <summary>
/// Lists the port rules currently installed on the host firewall.
/// </summary>
/// <remarks>
/// Takes no parameters. The host facts the agent needs to tell an administrator's rule from the
/// ruleset's own unconditional accepts come from <c>FirewallOptions</c>, never from the request:
/// a caller able to name them could name the wrong ones and be shown the SSH accept as a rule they
/// may delete.
/// </remarks>
public sealed record ListRulesQuery;
