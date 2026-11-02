namespace Maran.Modules.Backups.Domain.ValueObjects;

/// <summary>A cadence that takes a backup once every week, on one named weekday.</summary>
/// <param name="DayOfWeekUtc">The day of the week, in UTC, on which the backup is taken.</param>
/// <param name="HourUtc">The hour of that day, in UTC, at which the backup is taken.</param>
public sealed record WeeklyBackupCadence(DayOfWeek DayOfWeekUtc, int HourUtc) : BackupCadence;
