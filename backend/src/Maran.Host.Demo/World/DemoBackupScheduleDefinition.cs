using Maran.Modules.Backups.Domain.Enums;

namespace Maran.Host.Demo.World;

/// <summary>The recurring backup schedule of a demo account.</summary>
/// <param name="Frequency">How often the backup runs.</param>
/// <param name="HourUtc">The UTC hour it starts.</param>
/// <param name="DayOfWeekUtc">The weekday of a weekly schedule, null for a daily one.</param>
/// <param name="RetainCount">How many backups are kept.</param>
public sealed record DemoBackupScheduleDefinition(
    BackupFrequency Frequency,
    int HourUtc,
    DayOfWeek? DayOfWeekUtc,
    int RetainCount);
