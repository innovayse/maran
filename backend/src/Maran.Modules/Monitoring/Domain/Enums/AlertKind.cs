
namespace Maran.Modules.Monitoring.Domain.Enums;

/// <summary>What kind of condition an <see cref="Entities.AlertState"/> row is watching.</summary>
/// <remarks>
/// A closed set, and deliberately small: the panel alerts on the two conditions spec §11 names — a
/// filesystem about to fill and a managed service that has stopped. The kind is half of a row's
/// identity (the subject is the other half), so adding a value here is adding a new row per
/// subject, never a new meaning for an existing one.
/// </remarks>
public enum AlertKind
{
    /// <summary>The root filesystem is above the disk-usage threshold.</summary>
    DiskUsage = 1,

    /// <summary>A service the agent watches is reported stopped.</summary>
    ServiceStopped = 2,
}
