using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Modules.Dns;

/// <summary>
/// Holds the Dns module's single published <see cref="Manifest"/> instance (rules/csharp.md "Canonical
/// backend layout" — module identity). Kept as its own type, distinct from <see cref="DnsModule"/>, because
/// <see cref="Manifest"/> is plain data with no framework dependency, while <see cref="DnsModule"/> is the
/// DI entry point.
/// </summary>
public static class DnsManifest
{
    /// <summary>The Dns module's published identity.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="LicenceTier.Included"/>: authoritative DNS for the domains a panel already hosts is
    /// table-stakes for a hosting panel, and a customer whose sites the panel serves expects to manage
    /// their own records.
    /// </para>
    /// <para>
    /// <see cref="AgentCapability.Dns"/> and NOTHING else, which an administrator reads before installing
    /// and <c>AgentCapabilityGuard</c> refuses to compose past. In particular NOT
    /// <see cref="AgentCapability.Firewall"/>: the agent's own enable operation opens port 53 as part of
    /// switching the name server on, so this module never reaches the firewall. And NOT
    /// <see cref="AgentCapability.Ssl"/> or <see cref="AgentCapability.Sites"/>: whether a site claims a
    /// host name is asked through <c>ISiteHostnameDirectory</c>, which hands back a boolean and no site.
    /// </para>
    /// <para>
    /// <see cref="ModuleKind.Packageable"/>: DNS is an optional server feature, not part of the core.
    /// <see cref="Manifest.SchemaName"/> is the <c>dns</c> schema. The zones entry is offered to everyone
    /// (a customer sees their own zones, an administrator every zone, through the tenant filter) and the
    /// name-server settings entry to administrators only. No entry is offered to a reseller, whose context
    /// shows no zone (rules/security.md item 6).
    /// </para>
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "dns",
        DisplayNameKey: "DnsModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Packageable,
        MinPanelVersion: "1.0.0-beta.1",
        SchemaName: "dns",
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Dns],
        Permissions: [AuthorizationPolicies.AdminOnly, AuthorizationPolicies.AnyAuthenticated],
        Menu: [
            new ModuleMenuItem(
                Key: "dns-zones",
                RouteName: "dns-zones",
                IconKey: ModuleIconKeys.Globe,
                LabelKey: "DnsMenuZones",
                Order: 10,
                Audience: ModuleAudience.Everyone),
            new ModuleMenuItem(
                Key: "dns-nameservers",
                RouteName: "dns-nameservers",
                IconKey: ModuleIconKeys.Server,
                LabelKey: "DnsMenuNameservers",
                Order: 20,
                Audience: ModuleAudience.AdministratorOnly),
        ],
        Routes: [
            new ModuleRoute(
                Name: "dns-zones",
                Path: "/dns/zones",
                TitleKey: "DnsRouteZones",
                RequiresModule: null),
            new ModuleRoute(
                Name: "dns-nameservers",
                Path: "/dns/nameservers",
                TitleKey: "DnsRouteNameservers",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.Everyone);
}
