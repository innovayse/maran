using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity;

/// <summary>
/// Holds the Identity module's single published <see cref="Manifest"/> instance
/// (rules/csharp.md "Canonical backend layout" — module identity). Kept as its own type, distinct
/// from <see cref="IdentityModule"/>, because <see cref="Manifest"/> is plain data with no
/// framework dependency, while <see cref="IdentityModule"/> is the DI/routing entry point.
/// </summary>
public static class IdentityManifest
{
    /// <summary>The Identity module's published identity.</summary>
    public static Manifest Instance { get; } = new(
        Id: "identity",
        DisplayNameKey: "IdentityModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: [],
        AgentCapabilities: []);
}
