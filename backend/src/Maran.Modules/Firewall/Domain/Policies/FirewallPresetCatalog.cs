using Maran.Agent.Client.Services.FirewallService;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.Domain.ValueObjects;

namespace Maran.Modules.Firewall.Domain.Policies;

/// <summary>
/// The one place the panel decides which ports its rule presets open. The browser holds none of
/// them (rules/architecture.md "The backend owns the data, the SPA renders it"): it lists what this
/// catalog answers and sends each rule through the ordinary allow request, whose validator stays the
/// authority on what is accepted.
/// </summary>
public static class FirewallPresetCatalog
{
    /// <summary>The source range that admits every IPv4 source, which a public service needs.</summary>
    private const string AnySource = "0.0.0.0/0";

    /// <summary>Plain HTTP's port.</summary>
    private const int HttpPort = 80;

    /// <summary>HTTPS's port.</summary>
    private const int HttpsPort = 443;

    /// <summary>The port MySQL listens on.</summary>
    private const int MysqlPort = 3306;

    /// <summary>Every preset the rule screen offers, in the order it lists them.</summary>
    public static readonly IReadOnlyList<FirewallPreset> All =
    [
        new FirewallPreset(
            FirewallPresetKey.Web,
            Closable: false,
            [
                new FirewallPresetRule(HttpPort, null, AgentFirewallProtocol.Tcp, AnySource),
                new FirewallPresetRule(HttpsPort, null, AgentFirewallProtocol.Tcp, AnySource),
            ]),
        new FirewallPreset(
            FirewallPresetKey.MysqlExternal,
            Closable: true,
            [new FirewallPresetRule(MysqlPort, null, AgentFirewallProtocol.Tcp, AnySource)]),
    ];
}
