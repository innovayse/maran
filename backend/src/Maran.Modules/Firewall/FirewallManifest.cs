using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Modules.Firewall;

/// <summary>
/// Holds the Firewall module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="FirewallModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="FirewallModule"/> is the DI/routing entry point.
/// </summary>
public static class FirewallManifest
{
    /// <summary>The Firewall module's published identity.</summary>
    public static Manifest Instance { get; } = new(
        Id: "firewall",
        DisplayNameKey: "FirewallModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Packageable,
        MinPanelVersion: "1.0.0-beta.4",
        SchemaName: "firewall",
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Firewall],
        Permissions: [AuthorizationPolicies.AdminOnly],
        Menu: [
            new ModuleMenuItem(
                Key: "firewall-rules",
                RouteName: "firewall",
                IconKey: ModuleIconKeys.ShieldCheck,
                LabelKey: "FirewallMenuRules",
                Order: 10,
                Audience: ModuleAudience.AdministratorOnly),
            new ModuleMenuItem(
                Key: "firewall-bans",
                RouteName: "firewall-bans",
                IconKey: ModuleIconKeys.ShieldCheck,
                LabelKey: "FirewallMenuBans",
                Order: 20,
                Audience: ModuleAudience.AdministratorOnly),
            new ModuleMenuItem(
                Key: "firewall-whitelist",
                RouteName: "firewall-whitelist",
                IconKey: ModuleIconKeys.ShieldCheck,
                LabelKey: "FirewallMenuWhitelist",
                Order: 30,
                Audience: ModuleAudience.AdministratorOnly),
        ],
        Routes: [
            new ModuleRoute(
                Name: "firewall",
                Path: "/firewall",
                TitleKey: "FirewallRouteRules",
                RequiresModule: null),
            new ModuleRoute(
                Name: "firewall-bans",
                Path: "/firewall/bans",
                TitleKey: "FirewallRouteBans",
                RequiresModule: null),
            new ModuleRoute(
                Name: "firewall-whitelist",
                Path: "/firewall/whitelist",
                TitleKey: "FirewallRouteWhitelist",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.AdministratorOnly);
}
