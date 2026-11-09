using Maran.Sdk.Contracts;

namespace Maran.Modules.Fleet;

/// <summary>
/// Holds the Fleet module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="FleetModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="FleetModule"/> is the DI entry point.
/// </summary>
public static class FleetManifest
{
    /// <summary>The Fleet module's published identity.</summary>
    /// <remarks>
    /// <para>
    /// The id is also the name of the module's PostgreSQL schema, <c>fleet</c>. It is
    /// <see cref="ModuleKind.Core"/> because the registry of servers is a facility other modules need
    /// to place an account and to aim an agent call (rules/architecture.md "A facility more than one
    /// module needs is its own module"), so it cannot be removed from a panel that has them.
    /// </para>
    /// <para>
    /// No <see cref="AgentCapability"/> is declared yet, deliberately: this first cut of the module
    /// holds the registry rows and drives nothing. The declaration is part of what an administrator
    /// reads before installing (rules/security.md item 13), and a declaration that precedes its use
    /// is refused by <c>AgentCapabilityTests</c>. <see cref="AgentCapability.System"/> is added by the
    /// change that introduces the health prober, the first code here that asks an agent anything.
    /// </para>
    /// <para>
    /// The menu, the routes and the permissions are empty for the same reason: the module has no
    /// endpoint and no screen yet. The administrator-only <c>GET /api/v1/servers</c> still lives in
    /// Monitoring until the operations that replace it land.
    /// </para>
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "fleet",
        DisplayNameKey: "FleetModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Core,
        MinPanelVersion: "1.0.0-beta.4",
        SchemaName: "fleet",
        Dependencies: [],
        AgentCapabilities: [],
        Permissions: [],
        Menu: [],
        Routes: [],
        Audience: ModuleAudience.AdministratorOnly);
}
