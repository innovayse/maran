using Maran.Sdk.Contracts;

namespace Maran.Modules.Backups;

/// <summary>
/// Holds the Backups module's single published <see cref="Manifest"/> instance (rules/csharp.md
/// "Canonical backend layout" — module identity). Kept as its own type, distinct from
/// <see cref="BackupsModule"/>, because <see cref="Manifest"/> is plain data with no framework
/// dependency, while <see cref="BackupsModule"/> is the DI/routing entry point.
/// </summary>
public static class BackupsManifest
{
    /// <summary>The Backups module's published identity.</summary>
    /// <remarks>
    /// Depends on <c>accounts</c>: every backup is a backup OF an account, and the system user name
    /// each agent call is addressed by is read through the Sdk's account directory. The dependency
    /// is declared here — where the module catalogue and the administrator installing the module can
    /// see it — and is NOT a project reference, which the architecture tests forbid.
    ///
    /// Two agent capabilities. The archive's bytes are read and written by the agent's backup area
    /// alone; this module never reaches the files, database or sites areas, so it declares none of
    /// them and <c>AgentCapabilityGuard</c> refuses to compose it if it ever does (rules/security.md
    /// item 13). <c>System</c> is the handshake, and it is declared because the directory a local
    /// destination names is the AGENT's constant: the panel reads it from
    /// <c>AgentInfo.backup_root</c> when a screen shows it, rather than keeping a copy it cannot
    /// check. It is a read of the agent's own identity — it mutates nothing — and an administrator
    /// installing this module sees that it asks to know what agent it is talking to.
    /// </remarks>
    public static Manifest Instance { get; } = new(
        Id: "backups",
        DisplayNameKey: "BackupsModuleDisplayName",
        Version: "1.0.0",
        Tier: LicenceTier.Included,
        Dependencies: ["accounts"],
        AgentCapabilities: [AgentCapability.Backup, AgentCapability.System],
        Audience: ModuleAudience.Everyone);
}
