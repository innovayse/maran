using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Entities;

namespace Maran.Modules.Backups.Mappers;

/// <summary>Restates a <see cref="BackupSchedule"/> as the wire shape a settings screen reads.</summary>
/// <remarks>
/// A mapper translates; it never decides (rules/csharp.md). Every field is copied across unchanged —
/// in particular this does NOT compute whether the schedule is due, which is
/// <see cref="BackupSchedule.IsDue"/>'s answer and belongs to the entity. A due-ness recomputed here
/// would be a second statement of the rule that the nightly pass does not read, so the two could
/// disagree and only the screen would show it.
/// </remarks>
public static class BackupScheduleMapper
{
    /// <summary>Builds the outward view of one schedule.</summary>
    /// <param name="schedule">The recorded schedule.</param>
    /// <returns>The wire shape carrying exactly what the row holds.</returns>
    public static BackupScheduleDto From(BackupSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return new BackupScheduleDto(
            schedule.Id,
            schedule.AccountId,
            schedule.DestinationId,
            schedule.Frequency,
            schedule.HourUtc,
            schedule.DayOfWeekUtc,
            schedule.RetainCount,
            schedule.Enabled,
            schedule.LastRunAt);
    }
}
