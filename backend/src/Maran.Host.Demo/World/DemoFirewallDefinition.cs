namespace Maran.Host.Demo.World;

/// <summary>The firewall state of the demo world.</summary>
/// <param name="PortRules">Port rules.</param>
/// <param name="Bans">Banned addresses.</param>
/// <param name="Whitelist">Whitelisted ranges.</param>
public sealed record DemoFirewallDefinition(
    IReadOnlyList<DemoPortRuleDefinition> PortRules,
    IReadOnlyList<DemoBanDefinition> Bans,
    IReadOnlyList<DemoWhitelistDefinition> Whitelist);
