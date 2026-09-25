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
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "licensing",
        DisplayNameKey: "LicensingModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: [],
        AgentCapabilities: [],
        Audience: ModuleAudience.AdministratorOnly);
}
