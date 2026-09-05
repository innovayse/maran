using Maran.Sdk.Contracts;

namespace Maran.Modules.Sftp;

/// <summary>
/// Holds the Sftp module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="SftpModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="SftpModule"/> is the DI/routing entry point.
/// </summary>
public static class SftpManifest
{
    /// <summary>The Sftp module's published identity.</summary>
    public static Manifest Instance { get; } = new(
        Id: "sftp",
        DisplayNameKey: "SftpModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Sftp]);
}
