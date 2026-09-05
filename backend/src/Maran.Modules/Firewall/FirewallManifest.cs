using Maran.Sdk.Contracts;

namespace Maran.Modules.Firewall;

/// <summary>
/// Holds the Firewall module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="FirewallModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="FirewallModule"/> is the DI/routing entry point.
/// </summary>
public static class FirewallManifest
{
    /// <summary>The Firewall module's published identity.</summary>
    public static Manifest Instance { get; } = new(
        Id: "firewall",
        DisplayNameKey: "FirewallModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Firewall]);
}
