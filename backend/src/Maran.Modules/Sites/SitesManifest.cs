using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Modules.Sites;

/// <summary>
/// Holds the Sites module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="SitesModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="SitesModule"/> is the DI/routing entry point.
/// </summary>
public static class SitesManifest
{
    /// <summary>The Sites module's published identity.</summary>
    /// <remarks>
    /// <see cref="ModuleKind.Core"/>: certificates, backups and file-transfer logins are all shaped around a
    /// site, so the panel is not usable with it removed. <see cref="Manifest.SchemaName"/> is the <c>sites</c>
    /// schema. The <c>site-php</c> entry is offered to customers only in the SPA; the audience here is the nearest
    /// <see cref="ModuleAudience"/> value and the endpoints enforce the role on their own.
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "sites",
        DisplayNameKey: "SitesModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Core,
        MinPanelVersion: "1.0.0-beta.1",
        SchemaName: "sites",
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Php, AgentCapability.Sites],
        Permissions: [AuthorizationPolicies.AdminOnly, AuthorizationPolicies.AnyAuthenticated],
        Menu: [
            new ModuleMenuItem(
                Key: "sites-list",
                RouteName: "sites",
                IconKey: ModuleIconKeys.Earth,
                LabelKey: "SitesMenuSitesList",
                Order: 10,
                Audience: ModuleAudience.Everyone),
            new ModuleMenuItem(
                Key: "site-php",
                RouteName: "php-settings",
                IconKey: ModuleIconKeys.Earth,
                LabelKey: "SitesMenuSitePhp",
                Order: 20,
                Audience: ModuleAudience.Everyone),
        ],
        Routes: [
            new ModuleRoute(
                Name: "sites",
                Path: "/sites",
                TitleKey: "SitesRouteSites",
                RequiresModule: null),
            new ModuleRoute(
                Name: "sites-new",
                Path: "/sites/new",
                TitleKey: "SitesRouteSitesNew",
                RequiresModule: null),
            new ModuleRoute(
                Name: "php-settings",
                Path: "/sites/php-settings",
                TitleKey: "SitesRoutePhpSettings",
                RequiresModule: null),
            new ModuleRoute(
                Name: "site-detail",
                Path: "/sites/:id",
                TitleKey: "SitesRouteSiteDetail",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.Everyone);
}
