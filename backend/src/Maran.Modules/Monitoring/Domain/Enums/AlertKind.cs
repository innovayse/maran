
namespace Maran.Modules.Monitoring.Domain.Enums;

/// <summary>What kind of condition an <see cref="Entities.AlertState"/> row is watching.</summary>
/// <remarks>
/// A closed set. Spec §11 names the first two — a filesystem about to fill and a managed service
/// that has stopped. <see cref="SftpJailDrifted"/> is a later addition, for release-readiness issue
/// #28 item E: the installer writes the sshd `Match Group` block that jails every SFTP login once,
/// at setup, and nothing re-checked it afterwards — a package upgrade or a hand edit removing it
/// silently turned every SFTP login into a full shell session while the panel kept reporting every
/// account as jailed. The kind is half of a row's identity (the subject is the other half), so
/// adding a value here is adding a new row per subject, never a new meaning for an existing one.
/// </remarks>
public enum AlertKind
{
    /// <summary>The root filesystem is above the disk-usage threshold.</summary>
    DiskUsage = 1,

    /// <summary>A service the agent watches is reported stopped.</summary>
    ServiceStopped = 2,

    /// <summary>
    /// The sshd `Match Group` block that jails every SFTP login is missing, or present but missing
    /// one or more of the directives that make it a jail.
    /// </summary>
    SftpJailDrifted = 3,

    /// <summary>
    /// The filesystem holding hosting accounts' home directories cannot enforce a per-user disk
    /// quota — mounted without quota accounting, or mounted correctly but quota accounting is off.
    /// This is true continuously for as long as the condition holds (unlike <see cref="DiskUsage"/>,
    /// which is transient), and a remount can flip it without touching any account, which is why it
    /// gets its own row rather than being folded into the disk-usage check.
    /// </summary>
    QuotaNotEnforceable = 4,

    /// <summary>
    /// The closed PluginLoader's comparison of installed files against the release's signed hash
    /// list (docs/superpowers/specs/2026-09-19-maran-code-integrity.md) either found a difference,
    /// or could not run the comparison at all. Two subjects share this one kind —
    /// <see cref="Services.AlertEvaluator.InstalledFilesSubject"/> for an actual mismatch and
    /// <see cref="Services.AlertEvaluator.HashListSubject"/> for the hash list itself being missing
    /// or unreadable — because both answer the same operator question ("can this panel currently
    /// vouch for its own files?") and both must resolve independently. This is a REPORT OF FACT, not
    /// an accusation: BSL permits a customer to modify their own installation, and this kind exists
    /// to tell them a difference exists, never to claim their license was violated.
    /// </summary>
    CodeIntegrityDrifted = 5,
}
