namespace Maran.Modules.Backups.Domain.Enums;

/// <summary>How often a <see cref="Entities.BackupSchedule"/> takes a backup.</summary>
/// <remarks>
/// <para>
/// <b>A closed set of two, deliberately, rather than a cron expression.</b> A cron expression is a
/// parsing surface — five fields, ranges, steps, lists, names — and every one of those forms would
/// have to be validated at the API boundary, re-validated wherever the schedule is evaluated, and
/// rendered back into a sentence a screen can show. What the product actually promises is "daily at
/// 03:00", and an enum plus an hour says exactly that with nothing to parse and nothing to get
/// wrong.
/// </para>
/// <para>
/// It is also NOT the customer's crontab. A backup runs as root, reads the whole of an account's
/// home and dumps its databases as the database superuser; a schedule the account could edit would
/// be a root operation the account writes the arguments of.
/// </para>
/// </remarks>
public enum BackupFrequency
{
    /// <summary>Once every day, at the schedule's hour.</summary>
    Daily = 0,

    /// <summary>Once every week, on the schedule's day of the week and at its hour.</summary>
    Weekly = 1,
}
