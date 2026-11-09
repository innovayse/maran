using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Modules.Monitoring;

/// <summary>
/// Holds the Monitoring module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="MonitoringModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="MonitoringModule"/> is the DI/routing entry point.
/// </summary>
public static class MonitoringManifest
{
    /// <summary>The Monitoring module's published identity.</summary>
    /// <remarks>
    /// Its two screens are Metrics and the pending-updates page, which the SPA presents under Servers.
    /// <see cref="ApiKeyPolicyNames.ServerRead"/> is declared because the server-status controller is
    /// reached with an API key.
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "monitoring",
        DisplayNameKey: "MonitoringModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Packageable,
        MinPanelVersion: "1.0.0-beta.4",
        SchemaName: "monitoring",
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Monitor],
        Permissions: [AuthorizationPolicies.AdminOnly, ApiKeyPolicyNames.ServerRead],
        Menu: [
            new ModuleMenuItem(
                Key: "monitoring-metrics",
                RouteName: "monitoring",
                IconKey: ModuleIconKeys.ChartLine,
                LabelKey: "MonitoringMenuMetrics",
                Order: 10,
                Audience: ModuleAudience.AdministratorOnly),
            new ModuleMenuItem(
                Key: "monitoring-updates",
                RouteName: "servers-updates",
                IconKey: ModuleIconKeys.Server,
                LabelKey: "MonitoringMenuUpdates",
                Order: 20,
                Audience: ModuleAudience.AdministratorOnly),
        ],
        Routes: [
            new ModuleRoute(
                Name: "monitoring",
                Path: "/monitoring",
                TitleKey: "MonitoringRouteMetrics",
                RequiresModule: null),
            new ModuleRoute(
                Name: "servers-updates",
                Path: "/servers/updates",
                TitleKey: "MonitoringRouteUpdates",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.AdministratorOnly);
}
