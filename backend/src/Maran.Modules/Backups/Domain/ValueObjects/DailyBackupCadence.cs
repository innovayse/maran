namespace Maran.Modules.Backups.Domain.ValueObjects;

/// <summary>A cadence that takes a backup once every day.</summary>
/// <param name="HourUtc">The hour of the day, in UTC, at which the backup is taken.</param>
public sealed record DailyBackupCadence(int HourUtc) : BackupCadence;
