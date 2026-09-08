using Maran.Modules.Backups.Domain.Enums;

namespace Maran.Modules.Backups.Common;

/// <summary>Outward view of one backup schedule: everything the settings screen shows and edits.</summary>
/// <remarks>
/// It carries <see cref="LastRunAt"/> because a schedule's most useful fact is whether it has
/// actually run — a screen showing only "enabled, daily at 03:00" tells an operator what was
/// configured and nothing about whether it works, which is the whole question a backup schedule
/// raises. It carries the destination as an identifier and not a path: the screen resolves it
/// against the destinations list, and a path repeated here would be a second statement of where the
/// server keeps archives.
/// </remarks>
/// <param name="Id">The schedule's identity.</param>
/// <param name="AccountId">The account it backs up, or <c>null</c> for every account on the host.</param>
/// <param name="DestinationId">The destination it writes to, or <c>null</c> for the default one.</param>
/// <param name="Frequency">How often a backup is taken.</param>
/// <param name="HourUtc">The hour of the day, in UTC, at which it is taken.</param>
/// <param name="DayOfWeekUtc">The weekday for a weekly schedule; <c>null</c> for a daily one.</param>
/// <param name="RetainCount">How many successful backups are kept before the oldest are pruned.</param>
/// <param name="Enabled">Whether the schedule actually runs.</param>
/// <param name="LastRunAt">When a run was last started, or <c>null</c> if it never has been.</param>
public sealed record BackupScheduleDto(
    Guid Id,
    Guid? AccountId,
    Guid? DestinationId,
    BackupFrequency Frequency,
    int HourUtc,
    DayOfWeek? DayOfWeekUtc,
    int RetainCount,
    bool Enabled,
    DateTimeOffset? LastRunAt);
