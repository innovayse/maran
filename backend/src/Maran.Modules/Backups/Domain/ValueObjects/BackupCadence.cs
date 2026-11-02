namespace Maran.Modules.Backups.Domain.ValueObjects;

/// <summary>
/// When a <see cref="Entities.BackupSchedule"/> takes its backup: every day at an hour, or on one
/// named weekday at an hour. One of exactly two cases, each its own type, so the weekday exists only
/// on the case that has one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a union and not a frequency beside a nullable weekday.</b> The pair (weekly, no
/// weekday) was representable, and the schedule read it as daily without saying so: a weekly
/// schedule that silently ran every night. Here the weekday is a member of <see cref="WeeklyBackupCadence"/>
/// and of nothing else, so the question "which day?" cannot be asked of a daily cadence and cannot
/// go unanswered for a weekly one.
/// </para>
/// <para>
/// A value the business has rules about: the occurrence arithmetic in <see cref="Entities.BackupSchedule"/>
/// is stated over these cases, and splitting one into its fields is exactly how the invalid pair
/// came to exist (rules/csharp.md "Domain/ValueObjects").
/// </para>
/// </remarks>
public abstract record BackupCadence
{
    /// <summary>Closes the hierarchy to the two cases this module declares.</summary>
    private protected BackupCadence()
    {
    }
}
