
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
}
