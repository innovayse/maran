using Maran.Sdk.Contracts;

namespace Maran.Modules.Monitoring;

/// <summary>
/// Holds the Monitoring module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="MonitoringModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="MonitoringModule"/> is the DI/routing entry point.
/// </summary>
public static class MonitoringManifest
{
    /// <summary>The Monitoring module's published identity.</summary>
    public static Manifest Instance { get; } = new(
        Id: "monitoring",
        DisplayNameKey: "MonitoringModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Monitor]);
}
