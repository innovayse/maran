using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;

namespace Maran.Modules.Backups.Domain.Policies;

/// <summary>
/// What a customer may ask of their own backup schedule, as distinct from what an administrator may
/// set (issue #147).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the customer's bounds are narrower than the entity's.</b> A schedule keeps up to
/// <see cref="BackupSchedule.MaximumRetainCount"/> copies, a number that is the operator's decision
/// about the machine's disk. A customer who chose it would be filling a disk they do not pay for in
/// full, and a customer who chose one copy would be deleting their own history by their own hand.
/// The customer's ceiling is therefore a separate, smaller number, and the entity's constant stays
/// the bound on the administrator's surface.
/// </para>
/// <para>
/// <b>The frequencies are an explicit allow-list, not "every member of the enum".</b> A cadence added
/// to <see cref="BackupFrequency"/> later (an hourly one, say) would otherwise become available to
/// every customer the day it is added, with nobody having decided that it should be.
/// </para>
/// </remarks>
public static class CustomerBackupLimits
{
    /// <summary>The most copies a customer's own schedule may keep.</summary>
    public const int MaximumRetainCount = 7;

    /// <summary>The cadences a customer may choose, in the order a screen offers them.</summary>
    private static readonly BackupFrequency[] Frequencies = [BackupFrequency.Daily, BackupFrequency.Weekly];

    /// <summary>The cadences a customer may choose.</summary>
    /// <returns>Every cadence the customer surface accepts, in display order.</returns>
    public static IReadOnlyList<BackupFrequency> AllowedFrequencies()
    {
        return Frequencies;
    }

    /// <summary>Whether a customer may choose a cadence.</summary>
    /// <param name="frequency">The cadence a request names.</param>
    /// <returns><c>true</c> when the cadence is on the customer allow-list.</returns>
    public static bool AllowsFrequency(BackupFrequency frequency)
    {
        return Frequencies.Contains(frequency);
    }
}
