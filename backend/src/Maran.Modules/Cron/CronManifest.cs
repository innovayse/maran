using Maran.Sdk.Contracts;

namespace Maran.Modules.Cron;

/// <summary>
/// Holds the Cron module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="CronModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="CronModule"/> is the DI/routing entry point.
/// </summary>
public static class CronManifest
{
    /// <summary>The Cron module's published identity.</summary>
    /// <remarks>
    /// The id names no PostgreSQL schema, unlike every module shipped before it: this one owns no
    /// tables (see <see cref="CronModule"/>). The id is still the module's stable machine name, and
    /// it is what the catalogue and the licence system address it by.
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "cron",
        DisplayNameKey: "CronModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: [],
        AgentCapabilities: [AgentCapability.Cron]);
}
