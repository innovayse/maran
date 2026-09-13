using Maran.Sdk.Contracts;

namespace Maran.Modules.Ssl;

/// <summary>
/// Holds the Ssl module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="SslModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="SslModule"/> is the DI/routing entry point.
/// </summary>
public static class SslManifest
{
    /// <summary>The Ssl module's published identity.</summary>
    /// <remarks>
    /// Depends on <c>sites</c>: a certificate is installed for a site, and every operation here reads
    /// a site through the Sdk's site directory. The dependency is declared here — where the module
    /// catalogue can see it — and is NOT a project reference, which the architecture tests forbid.
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "ssl",
        DisplayNameKey: "SslModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: ["sites"],
        AgentCapabilities: [AgentCapability.Files, AgentCapability.Sites, AgentCapability.Ssl]);
}
