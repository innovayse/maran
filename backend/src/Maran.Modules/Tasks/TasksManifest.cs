using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Modules.Tasks;

/// <summary>
/// Holds the Tasks module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="TasksModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="TasksModule"/> is the DI/routing entry point.
/// </summary>
public static class TasksManifest
{
    /// <summary>The Tasks module's published identity.</summary>
    /// <remarks>
    /// <see cref="ModuleKind.Core"/>: every module records its long operations through the Sdk's
    /// task recorder, which this module answers, so it is still needed if any other module is removed
    /// (rules/architecture.md "A facility more than one module needs").
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "tasks",
        DisplayNameKey: "TasksModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Core,
        MinPanelVersion: "1.0.0-beta.4",
        SchemaName: "tasks",
        Dependencies: [],
        AgentCapabilities: [],
        Permissions: [AuthorizationPolicies.AnyAuthenticated],
        Menu: [
            new ModuleMenuItem(
                Key: "tasks",
                RouteName: "tasks",
                IconKey: ModuleIconKeys.ListChecks,
                LabelKey: "TasksMenuList",
                Order: 10,
                Audience: ModuleAudience.Everyone),
        ],
        Routes: [
            new ModuleRoute(
                Name: "tasks",
                Path: "/tasks",
                TitleKey: "TasksRouteList",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.Everyone);
}
