using Maran.Sdk.Contracts;

namespace Maran.Modules.Tasks;

/// <summary>
/// Holds the Tasks module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="TasksModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="TasksModule"/> is the DI/routing entry point.
/// </summary>
public static class TasksManifest
{
    /// <summary>The Tasks module's published identity.</summary>
    public static Manifest Instance { get; } = new(
        Id: "tasks",
        DisplayNameKey: "TasksModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: [],
        AgentCapabilities: []);
}
