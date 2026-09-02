using Maran.Sdk.Contracts;

namespace Maran.Modules.Databases;

/// <summary>
/// Holds the Databases module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="DatabasesModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="DatabasesModule"/> is the DI/routing entry point.
/// </summary>
public static class DatabasesManifest
{
    /// <summary>The Databases module's published identity.</summary>
    public static Manifest Instance { get; } = new(
        Id: "databases",
        DisplayNameKey: "DatabasesModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: []);
}
