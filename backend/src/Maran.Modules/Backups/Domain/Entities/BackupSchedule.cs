using Maran.Modules.Backups.Domain.Enums;

namespace Maran.Modules.Backups.Domain.Entities;

/// <summary>
/// An operator's standing instruction to back an account up unattended, and how many of the results
/// to keep (spec §11).
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="AccountId"/> is nullable, and the null is the host-wide policy.</b> A schedule
/// naming no account applies to every account on the server, which is the setting an operator
/// actually wants to make once; a schedule naming one account is that account's own override. There
/// is at most one of each, enforced by a unique index, because two schedules for one account would
/// take two backups a night and neither of them would be wrong.
/// </para>
/// <para>
/// <b><see cref="Enabled"/> is false on a new schedule.</b> A schedule the operator did not ask for
/// that starts writing gigabytes at three in the morning is a surprise, and the honest ship state is
/// off.
/// </para>
/// <para>
/// <b>Due-ness is decided here and not in SQL.</b> The pass loads the enabled schedules — there are
/// as many of them as the host has accounts with an override, plus one — and asks each one. The
/// alternative, a translated predicate, would be a second statement of the rule that no test could
/// compare against this one, and the first symptom of the two disagreeing is a backup taken twice a
/// night or not at all. The same argument <see cref="Backup.SurvivesAccountDeletion"/> makes about
/// its own predicate.
/// </para>
/// <para>
/// <b><see cref="DestinationId"/> is nullable, and the null is the default destination.</b> It was
/// deliberately absent while no destination table existed, because a nullable foreign key to a table
/// that is not there and that nothing could write would have been a promise whose only evidence is a
/// column. The table exists now, so the schedule names what it writes to — with the same null
/// convention <see cref="Backup.DestinationId"/> uses, so "no destination named" means one thing
/// across the module rather than two.
/// </para>
/// </remarks>
public sealed class BackupSchedule
{
    /// <summary>How many successful backups a schedule keeps unless the operator says otherwise.</summary>
    /// <remarks>A week of dailies: enough that a fault noticed on Friday can be undone from Monday.</remarks>
    public const int DefaultRetainCount = 7;

    /// <summary>The fewest backups a schedule may be told to keep.</summary>
    /// <remarks>
    /// One, not zero. Zero would mean a schedule whose retention deletes the backup its own run has
    /// just taken, which is a nightly operation that costs a customer's disk and leaves nothing.
    /// </remarks>
    public const int MinimumRetainCount = 1;

    /// <summary>The most backups a schedule may be told to keep.</summary>
    /// <remarks>
    /// A year of dailies. A ceiling that exists is a refusal an operator can read; no ceiling is a
    /// number typed into a form that fills a disk months later.
    /// </remarks>
    public const int MaximumRetainCount = 365;

    /// <summary>The earliest hour of the day a schedule may name.</summary>
    public const int MinimumHourUtc = 0;

    /// <summary>The latest hour of the day a schedule may name.</summary>
    public const int MaximumHourUtc = 23;

    /// <summary>The schedule's identity.</summary>
    public Guid Id { get; private set; }

    /// <summary>The account this schedule backs up, or <c>null</c> for every account on the host.</summary>
    public Guid? AccountId { get; private set; }

    /// <summary>The destination this schedule writes to, or <c>null</c> for the default one.</summary>
    public Guid? DestinationId { get; private set; }

    /// <summary>How often a backup is taken.</summary>
    public BackupFrequency Frequency { get; private set; }

    /// <summary>The hour of the day, in UTC, at which the backup is taken.</summary>
    /// <remarks>
    /// UTC and not the server's local time, because the server's local time is a setting that can be
    /// changed underneath a schedule — and because a schedule read back through the API has to mean
    /// the same thing to a panel, a customer and an operator in three time zones.
    /// </remarks>
    public int HourUtc { get; private set; }

    /// <summary>
    /// The day of the week a <see cref="BackupFrequency.Weekly"/> schedule runs on, or <c>null</c>
    /// for a <see cref="BackupFrequency.Daily"/> one.
    /// </summary>
    /// <remarks>
    /// Named <c>DayOfWeekUtc</c> rather than <c>DayOfWeek</c> so that the property does not shadow
    /// the BCL type of the same name for every signature inside this class — the collision is silent
    /// and the first thing it breaks is a member declaration that reads perfectly.
    /// </remarks>
    public DayOfWeek? DayOfWeekUtc { get; private set; }

    /// <summary>How many successful backups of this account are kept before the oldest are pruned.</summary>
    public int RetainCount { get; private set; }

    /// <summary>Whether the schedule actually runs.</summary>
    public bool Enabled { get; private set; }

    /// <summary>
    /// The instant from which the next run is measured: the start of the most recent run, or the
    /// instant the schedule was last switched on, or <c>null</c> for a schedule that has never been
    /// enabled.
    /// </summary>
    /// <remarks>
    /// It is written BEFORE a run rather than after it. A panel that dies halfway through a backup
    /// would otherwise find the schedule still due at the next five-minute tick and start a second
    /// run on top of the first — and "the first one is still going" is exactly the state in which
    /// starting another is worst.
    /// </remarks>
    public DateTimeOffset? LastRunAt { get; private set; }

    /// <summary>Opens a schedule, disabled, with the panel's default cadence and retention.</summary>
    /// <param name="id">The schedule's identity.</param>
    /// <param name="accountId">The account it backs up, or <c>null</c> for every account.</param>
    /// <param name="destinationId">The destination it writes to, or <c>null</c> for the default one.</param>
    /// <param name="frequency">How often it runs.</param>
    /// <param name="hourUtc">The hour of the day, in UTC, it runs at.</param>
    /// <param name="dayOfWeekUtc">The day of the week for a weekly schedule; <c>null</c> for daily.</param>
    /// <param name="retainCount">How many successful backups to keep.</param>
    /// <remarks>
    /// Creation cannot enable a schedule. Switching one on is a separate act with a consequence — the
    /// server starts writing archives — and <see cref="Reconfigure"/> is where that act is recorded,
    /// including the stamp that stops the first run happening the moment the form is saved.
    /// </remarks>
    public BackupSchedule(
        Guid id,
        Guid? accountId,
        Guid? destinationId,
        BackupFrequency frequency,
        int hourUtc,
        DayOfWeek? dayOfWeekUtc,
        int retainCount)
    {
        Id = id;
        AccountId = accountId;
        DestinationId = destinationId;
        Frequency = frequency;
        HourUtc = hourUtc;
        DayOfWeekUtc = dayOfWeekUtc;
        RetainCount = retainCount;
        Enabled = false;
        LastRunAt = null;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private BackupSchedule()
    {
    }

    /// <summary>Replaces the schedule's destination, cadence, retention and enabled state in one edit.</summary>
    /// <param name="destinationId">The destination it writes to, or <c>null</c> for the default one.</param>
    /// <param name="frequency">How often it runs.</param>
    /// <param name="hourUtc">The hour of the day, in UTC, it runs at.</param>
    /// <param name="dayOfWeekUtc">The day of the week for a weekly schedule; <c>null</c> for daily.</param>
    /// <param name="retainCount">How many successful backups to keep.</param>
    /// <param name="enabled">Whether the schedule runs at all.</param>
    /// <param name="at">The instant of the edit, taken from <c>IClock</c>.</param>
    /// <remarks>
    /// <para>
    /// CRUD-shaped rather than a transition, because this is one form being saved and the domain has
    /// no opinion about which fields moved (rules/csharp.md "Domain models are rich"). The one
    /// opinion it does have is the stamp below.
    /// </para>
    /// <para>
    /// <b>Switching a schedule ON stamps <see cref="LastRunAt"/> with the moment it was switched
    /// on.</b> Without that, an operator enabling a 03:00 daily schedule at two in the afternoon
    /// would get a backup within five minutes — because 03:00 today is in the past and the schedule
    /// has never run — which is the surprise the off-by-default rule exists to avoid, arriving one
    /// click later. With it, the first run is the next 03:00, which is what the form said.
    /// </para>
    /// <para>
    /// Re-saving an already-enabled schedule does NOT re-stamp: that would let a form saved once a
    /// day postpone the backup for ever, and the postponement would be invisible.
    /// </para>
    /// </remarks>
    public void Reconfigure(
        Guid? destinationId,
        BackupFrequency frequency,
        int hourUtc,
        DayOfWeek? dayOfWeekUtc,
        int retainCount,
        bool enabled,
        DateTimeOffset at)
    {
        var switchedOn = enabled && !Enabled;

        DestinationId = destinationId;
        Frequency = frequency;
        HourUtc = hourUtc;
        DayOfWeekUtc = dayOfWeekUtc;
        RetainCount = retainCount;
        Enabled = enabled;

        if (switchedOn)
        {
            LastRunAt = at;
        }
    }

    /// <summary>Records that a run has been started, so this schedule is not due again for it.</summary>
    /// <param name="at">The instant the run began, taken from <c>IClock</c>.</param>
    /// <remarks>
    /// Called before the agent is asked for anything. The name says "started" and not "completed"
    /// because that is the fact being recorded: a run that fails does not make the schedule due
    /// again five minutes later, it makes it due again at its next occurrence, which is the only
    /// cadence that does not turn one broken account into a retry storm against a root process.
    /// </remarks>
    public void MarkRunStarted(DateTimeOffset at)
    {
        LastRunAt = at;
    }

    /// <summary>Whether a run should be started for this schedule now.</summary>
    /// <param name="now">The current instant, taken from <c>IClock</c>.</param>
    /// <returns><c>true</c> when the most recent occurrence has passed and has not been run.</returns>
    /// <remarks>
    /// <para>
    /// <b>The rule is "the most recent occurrence at or before now has not been run yet", not "the
    /// hour matches".</b> An hour match would fire on every five-minute tick within the hour — twelve
    /// backups a night — unless something else remembered, and the something else is
    /// <see cref="LastRunAt"/> either way. Comparing against the occurrence instead makes the
    /// remembering the whole rule: a tick at 03:05 and a tick at 03:10 compute the same 03:00, and
    /// only the first of them finds it unrun.
    /// </para>
    /// <para>
    /// It also means a panel that was switched off across 03:00 takes the backup at the next tick
    /// after it comes back, rather than skipping the night — which is the behaviour an operator
    /// expects of a nightly backup and NOT the behaviour of a system that only asks "is it 03:00
    /// now".
    /// </para>
    /// </remarks>
    public bool IsDue(DateTimeOffset now)
    {
        if (!Enabled)
        {
            return false;
        }

        var occurrence = MostRecentOccurrence(now);

        return LastRunAt is null || LastRunAt < occurrence;
    }

    /// <summary>The latest instant this schedule was supposed to run, at or before <paramref name="now"/>.</summary>
    /// <param name="now">The current instant.</param>
    /// <returns>The occurrence, always in the past or exactly now.</returns>
    /// <remarks>
    /// <para>
    /// Total by construction rather than by a fallback: for a daily schedule the answer is today's
    /// hour or yesterday's, and for a weekly one it is the named weekday's hour within the last
    /// seven days. Neither arm can fail to produce an answer.
    /// </para>
    /// <para>
    /// <b>A weekly schedule with no weekday is read as daily, and that is a real case rather than a
    /// defensive one.</b> The column is nullable, so the pair is representable in the database even
    /// though the validator refuses it at the boundary — a row edited in <c>psql</c>, or written by
    /// a future migration, can hold it. Backing up too often is the safe direction of that mistake;
    /// throwing would stop the whole nightly pass for every other account on the host.
    /// </para>
    /// </remarks>
    private DateTimeOffset MostRecentOccurrence(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        var todayAtHour = new DateTimeOffset(utc.Year, utc.Month, utc.Day, HourUtc, 0, 0, TimeSpan.Zero);

        if (Frequency == BackupFrequency.Daily || DayOfWeekUtc is null)
        {
            return utc >= todayAtHour ? todayAtHour : todayAtHour.AddDays(-1);
        }

        var daysBack = (((int)todayAtHour.DayOfWeek - (int)DayOfWeekUtc.Value) + 7) % 7;
        var candidate = todayAtHour.AddDays(-daysBack);

        return utc >= candidate ? candidate : candidate.AddDays(-7);
    }
}
