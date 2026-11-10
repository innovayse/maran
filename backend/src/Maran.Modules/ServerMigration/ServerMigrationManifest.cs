using Maran.Sdk.Contracts;
using Maran.Sdk.Navigation;

namespace Maran.Modules.ServerMigration;

/// <summary>
/// Holds the ServerMigration module's single published <see cref="Manifest"/> instance (rules/csharp.md "Canonical
/// backend layout" — module identity). Kept as its own type, distinct from <see cref="ServerMigrationModule"/>, because
/// <see cref="Manifest"/> is plain data with no framework dependency, while <see cref="ServerMigrationModule"/> is the DI
/// entry point.
/// </summary>
public static class ServerMigrationManifest
{
    /// <summary>The ServerMigration module's published identity.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="AgentCapability.Transfer"/> and NOTHING else, which an administrator reads before installing and
    /// <c>AgentCapabilityGuard</c> refuses to compose past: the one part of the agent that opens an outbound
    /// connection, and only to a host an administrator registered, with one pinned host key (rules/security.md
    /// item 10, item 13).
    /// </para>
    /// <para>
    /// <see cref="ModuleKind.Core"/> and <see cref="LicenceTier.Included"/>: reading a server one is moving away from
    /// is part of the panel. Administrator only throughout, because the panel holds a key to another machine: no
    /// customer or reseller is offered the module, its menu entry or its routes. The schema is the <c>servermigration</c>
    /// schema.
    /// </para>
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "servermigration",
        DisplayNameKey: "ServerMigrationModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Core,
        MinPanelVersion: "1.0.0-beta.4",
        SchemaName: "servermigration",
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Transfer],
        Permissions: [AuthorizationPolicies.AdminOnly, MigrationGrantPolicyNames.Pull],
        Menu: [
            new ModuleMenuItem(
                Key: "migration-remote-servers",
                RouteName: "migration-remote-servers",
                IconKey: ModuleIconKeys.Server,
                LabelKey: "ServerMigrationMenuRemoteServers",
                Order: 10,
                Audience: ModuleAudience.AdministratorOnly),
            new ModuleMenuItem(
                Key: "migration-jobs",
                RouteName: "migration-jobs",
                IconKey: ModuleIconKeys.Server,
                LabelKey: "ServerMigrationMenuJobs",
                Order: 20,
                Audience: ModuleAudience.AdministratorOnly),
        ],
        Routes: [
            new ModuleRoute(
                Name: "migration-remote-servers",
                Path: "/migration/remote-servers",
                TitleKey: "ServerMigrationRouteRemoteServers",
                RequiresModule: null),
            new ModuleRoute(
                Name: "migration-jobs",
                Path: "/migration/jobs",
                TitleKey: "ServerMigrationRouteJobs",
                RequiresModule: null),
        ],
        Audience: ModuleAudience.AdministratorOnly);
}
