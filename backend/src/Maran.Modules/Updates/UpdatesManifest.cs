using Maran.Sdk.Contracts;

namespace Maran.Modules.Updates;

/// <summary>
/// Holds the Updates module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="UpdatesModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while the module is the DI entry point.
/// </summary>
public static class UpdatesManifest
{
    /// <summary>The Updates module's published identity.</summary>
    /// <remarks>
    /// <see cref="ModuleKind.Core"/> and <see cref="LicenceTier.Included"/>: updating the panel is not a
    /// feature a licence can switch off. It owns no table, so <see cref="Manifest.SchemaName"/> is null,
    /// and it drives no part of the agent, so it declares no <see cref="AgentCapability"/>: the privileged
    /// step is a root systemd unit started by a request marker, never an agent call (issue #382). It has
    /// no menu entry and no route of its own — the SPA reaches its screen from Settings, as it does the
    /// panel certificate — which is the shape the Idempotency module has.
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "updates",
        DisplayNameKey: "UpdatesModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Core,
        MinPanelVersion: "1.0.0-beta.4",
        SchemaName: null,
        Dependencies: [],
        AgentCapabilities: [],
        Permissions: [AuthorizationPolicies.AdminOnly],
        Menu: [],
        Routes: [],
        Audience: ModuleAudience.AdministratorOnly);
}
