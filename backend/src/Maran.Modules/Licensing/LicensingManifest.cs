using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing;

/// <summary>
/// Holds the Licensing module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="LicensingModule"/>, for the same reason <c>DatabasesManifest</c> is: plain data with no
/// framework dependency, separate from the DI entry point.
/// </summary>
public static class LicensingManifest
{
    /// <summary>The Licensing module's published identity.</summary>
    /// <remarks>
    /// <see cref="LicenceTier.Included"/>, not <see cref="LicenceTier.AddOn"/> or
    /// <see cref="LicenceTier.PlanGated"/>: this module decides whether OTHER modules' tiers are
    /// unlocked, so it cannot itself be gated by the licence it evaluates — that would be circular,
    /// and it is also §229's own boundary restated as a manifest fact: the mechanism that may
    /// degrade is a paid module, never the core that decides what "paid" even means here.
    /// <para>
    /// <see cref="ModuleKind.Core"/>: it decides what the other modules may do and is never itself gated.
    /// <see cref="Manifest.SchemaName"/> is null because no stored licence row exists yet; when one does, the schema
    /// and its migrations service arrive together. It declares no menu entry and no route: it has no screen.
    /// </para>
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "licensing",
        DisplayNameKey: "LicensingModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Kind: ModuleKind.Core,
        MinPanelVersion: "1.0.0-beta.1",
        SchemaName: null,
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Monitor],
        Permissions: [AuthorizationPolicies.AdminOnly],
        Menu: [],
        Routes: [],
        Audience: ModuleAudience.AdministratorOnly);
}
