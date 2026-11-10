using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Modules.Apps;

/// <summary>
/// Holds the Apps module's single published <see cref="Manifest"/> instance (rules/csharp.md "Canonical
/// backend layout" - module identity). Kept as its own type, distinct from <see cref="AppsModule"/>,
/// because <see cref="Manifest"/> is plain data with no framework dependency, while
/// <see cref="AppsModule"/> is the DI entry point.
/// </summary>
public static class AppsManifest
{
    /// <summary>The Apps module's published identity.</summary>
    /// <remarks>
    /// <see cref="ModuleKind.Core"/> and <see cref="LicenceTier.Included"/>: a server cannot serve a site
    /// until a web server is installed, so the app store is not a feature a licence can switch off or an
    /// administrator can remove. <see cref="Manifest.Audience"/> is everyone because one endpoint,
    /// <c>GET /api/v1/apps/stack</c>, is read by every signed-in role (the SPA branches on it before it
    /// shows any screen); the store itself, the only menu entry, is administrator-only.
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "apps",
        DisplayNameKey: "AppsModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Core,
        MinPanelVersion: "1.0.0-beta.4",
        SchemaName: "apps",
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Apps],
        Permissions: [AuthorizationPolicies.AdminOnly, AuthorizationPolicies.AnyAuthenticated],
        Menu: [
            new ModuleMenuItem(
                Key: "apps",
                RouteName: "apps",
                IconKey: ModuleIconKeys.Grid,
                LabelKey: "AppsMenuStore",
                Order: 10,
                Audience: ModuleAudience.AdministratorOnly),
        ],
        Routes: [
            new ModuleRoute(
                Name: "apps",
                Path: "/apps",
                TitleKey: "AppsRouteStore",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.Everyone);
}
