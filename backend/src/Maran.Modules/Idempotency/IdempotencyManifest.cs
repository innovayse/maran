using Maran.Sdk.Contracts;

namespace Maran.Modules.Idempotency;

/// <summary>
/// Holds the Idempotency module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="IdempotencyModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while the module is the DI entry point.
/// </summary>
public static class IdempotencyManifest
{
    /// <summary>The Idempotency module's published identity.</summary>
    /// <remarks>
    /// The id is also the name of the module's PostgreSQL schema, <c>idempotency</c>. The module drives
    /// no part of the agent, so it declares no <see cref="AgentCapability"/> (rules/security.md item 13).
    /// It is <see cref="ModuleKind.Core"/>: the Sdk's idempotency filter reaches its store through an Sdk seam
    /// on every opted-in endpoint, and the module has no screen, so its menu and routes are empty.
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "idempotency",
        DisplayNameKey: "IdempotencyModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Core,
        MinPanelVersion: "1.0.0-beta.4",
        SchemaName: "idempotency",
        Dependencies: [],
        AgentCapabilities: [],
        Permissions: [],
        Menu: [],
        Routes: [],
        Audience: ModuleAudience.AdministratorOnly);
}
