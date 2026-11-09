using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Modules.Databases;

/// <summary>
/// Holds the Databases module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="DatabasesModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="DatabasesModule"/> is the DI/routing entry point.
/// </summary>
public static class DatabasesManifest
{
    /// <summary>The Databases module's published identity.</summary>
    /// <remarks>
    /// <see cref="ModuleKind.Packageable"/>: a panel without customer databases still serves sites.
    /// <see cref="Manifest.SchemaName"/> is the <c>databases</c> schema.
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "databases",
        DisplayNameKey: "DatabasesModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Packageable,
        MinPanelVersion: "1.0.0-beta.1",
        SchemaName: "databases",
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Db],
        Permissions: [AuthorizationPolicies.AdminOnly, AuthorizationPolicies.AnyAuthenticated],
        Menu: [
            new ModuleMenuItem(
                Key: "databases-list",
                RouteName: "databases",
                IconKey: ModuleIconKeys.Database,
                LabelKey: "DatabasesMenuDatabasesList",
                Order: 10,
                Audience: ModuleAudience.Everyone),
            new ModuleMenuItem(
                Key: "databases-users",
                RouteName: "database-users",
                IconKey: ModuleIconKeys.Database,
                LabelKey: "DatabasesMenuDatabasesUsers",
                Order: 15,
                Audience: ModuleAudience.Everyone),
            new ModuleMenuItem(
                Key: "databases-grant-repair",
                RouteName: "database-grant-repair",
                IconKey: ModuleIconKeys.Database,
                LabelKey: "DatabasesMenuDatabasesGrantRepair",
                Order: 20,
                Audience: ModuleAudience.AdministratorOnly),
        ],
        Routes: [
            new ModuleRoute(
                Name: "databases",
                Path: "/databases",
                TitleKey: "DatabasesRouteDatabases",
                RequiresModule: null),
            new ModuleRoute(
                Name: "database-users",
                Path: "/databases/users",
                TitleKey: "DatabasesRouteDatabaseUsers",
                RequiresModule: null),
            new ModuleRoute(
                Name: "database-grant-repair",
                Path: "/databases/grant-repair",
                TitleKey: "DatabasesRouteDatabaseGrantRepair",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.Everyone);
}
