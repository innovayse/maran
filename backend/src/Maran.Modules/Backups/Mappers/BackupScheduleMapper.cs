using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Domain.ValueObjects;

namespace Maran.Modules.Backups.Mappers;

/// <summary>Restates a <see cref="BackupSchedule"/> as the wire shape a settings screen reads.</summary>
/// <remarks>
/// A mapper translates; it never decides (rules/csharp.md). The wire keeps its flat frequency,
/// weekday and enabled fields, so the cadence union is spelled out as the three fields and the
/// instant the schedule was switched on as the one flag; every other field is copied across
/// unchanged, and the next run is <see cref="BackupSchedule.NextRunAt"/>'s answer, asked of the
/// entity rather than computed here. A next run recomputed here would be a second statement of the
/// rule that the sweep's <see cref="BackupSchedule.IsDue"/> does not read, so the two could disagree
/// and only the screen would show it.
/// </remarks>
public static class BackupScheduleMapper
{
    /// <summary>Builds the outward view of one schedule.</summary>
    /// <param name="schedule">The recorded schedule.</param>
    /// <returns>The wire shape carrying exactly what the row holds.</returns>
    public static BackupScheduleDto From(BackupSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        BackupFrequency frequency;
        int hourUtc;
        DayOfWeek? dayOfWeekUtc;

        switch (schedule.Cadence)
        {
            case DailyBackupCadence daily:
                frequency = BackupFrequency.Daily;
                hourUtc = daily.HourUtc;
                dayOfWeekUtc = null;
                break;

            case WeeklyBackupCadence weekly:
                frequency = BackupFrequency.Weekly;
                hourUtc = weekly.HourUtc;
                dayOfWeekUtc = weekly.DayOfWeekUtc;
                break;

            default:
                throw new InvalidOperationException(
                    $"Unhandled backup cadence {schedule.Cadence.GetType().Name}; the hierarchy has two cases.");
        }

        return new BackupScheduleDto(
            schedule.Id,
            schedule.AccountId,
            schedule.DestinationId,
            frequency,
            hourUtc,
            dayOfWeekUtc,
            schedule.RetainCount,
            schedule.EnabledAt is not null,
            schedule.LastRunAt,
            schedule.NextRunAt());
    }
}
