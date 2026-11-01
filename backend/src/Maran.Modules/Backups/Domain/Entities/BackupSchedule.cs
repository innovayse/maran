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

    /// <summary>How late a run may start after its named hour before that hour counts as missed.</summary>
    /// <remarks>
    /// Thirty minutes (issue #134). The scheduler ticks every five minutes, so a run that is on time
    /// starts within five; one that starts half an hour late did not start because the panel was
    /// not running, or because the sweep before it was still busy — either way the hour the operator
    /// named was not honoured, and that is what the alert reports.
    /// </remarks>
    public static readonly TimeSpan MissedRunTolerance = TimeSpan.FromMinutes(30);

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
    /// The instant the schedule was last switched on, or <c>null</c> while it is switched off.
    /// </summary>
    /// <remarks>
    /// It exists so that switching a schedule on can be remembered without pretending a backup ran.
    /// The first run is measured from it: an operator enabling a 03:00 daily schedule at two in the
    /// afternoon gets the next 03:00, not a backup within five minutes because 03:00 today is already
    /// in the past. Switching the schedule off clears it, so a value here always means "enabled since".
    /// </remarks>
    public DateTimeOffset? EnabledAt { get; private set; }

    /// <summary>
    /// The instant the most recent run was started, or <c>null</c> if this schedule has never started
    /// one.
    /// </summary>
    /// <remarks>
    /// Only <see cref="MarkRunStarted"/> writes it, so a value here is always a real run. It is written
    /// BEFORE a run rather than after it: a panel that dies halfway through a backup would otherwise
    /// find the schedule still due at the next five-minute tick and start a second run on top of the
    /// first — and "the first one is still going" is exactly the state in which starting another is
    /// worst.
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
    /// as <see cref="EnabledAt"/>, which stops the first run happening the moment the form is saved.
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
        EnabledAt = null;
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
    /// opinion it does have is when the schedule was switched on, below.
    /// </para>
    /// <para>
    /// <b>Switching a schedule ON records <see cref="EnabledAt"/>; switching it OFF clears it.</b>
    /// The next run is measured from the later of that instant and the last run, so a schedule
    /// switched on after its hour has passed waits for the next occurrence, which is what the form
    /// said. <see cref="LastRunAt"/> is not touched: no backup has run because a form was saved.
    /// </para>
    /// <para>
    /// Re-saving an already-enabled schedule does NOT move <see cref="EnabledAt"/>: that would let a
    /// form saved once a day postpone the backup for ever, and the postponement would be invisible.
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
            EnabledAt = at;
        }

        if (!enabled)
        {
            EnabledAt = null;
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
    /// <returns><c>true</c> when the next run has arrived; never for a disabled schedule.</returns>
    /// <remarks>
    /// <para>
    /// <b>The rule is "the most recent occurrence at or before now is later than the anchor", not
    /// "the hour matches".</b> An hour match would fire on every five-minute tick within the hour —
    /// twelve backups a night — unless something else remembered, and the something else is the run
    /// stamp either way. Comparing occurrences makes the remembering the whole rule: a tick at 03:05
    /// and a tick at 03:10 compute the same 03:00, and only the first of them finds it unrun.
    /// </para>
    /// <para>
    /// It is stated through <see cref="NextRunAt"/> — the next occurrence after the anchor has
    /// arrived — so the screen's "next run" and this sweep's decision are one computation, not two
    /// that could disagree. A panel that was switched off across 03:00 therefore takes the backup at
    /// the next tick after it comes back, rather than skipping the night.
    /// </para>
    /// </remarks>
    public bool IsDue(DateTimeOffset now)
    {
        var next = NextRunAt();

        return next is not null && next.Value <= now;
    }

    /// <summary>Says whether the occurrence now due was missed, and when it was due.</summary>
    /// <param name="now">The instant the sweep runs at.</param>
    /// <param name="dueAt">The named hour now owed; meaningful only when the answer is <c>true</c>.</param>
    /// <returns>
    /// <c>true</c> when an occurrence is owed and <paramref name="now"/> is more than
    /// <see cref="MissedRunTolerance"/> past it; <c>false</c> when nothing is owed or it is on time.
    /// </returns>
    /// <remarks>
    /// Read BEFORE <see cref="MarkRunStarted"/>, which moves the anchor and with it the occurrence.
    /// After a panel that was down for three nights it names the FIRST of them, because
    /// <see cref="NextRunAt"/> measures from the last run.
    /// </remarks>
    public bool TryGetMissedOccurrence(DateTimeOffset now, out DateTimeOffset dueAt)
    {
        var next = NextRunAt();
        dueAt = next.GetValueOrDefault();

        return next is not null && next.Value + MissedRunTolerance < now;
    }

    /// <summary>When this schedule will next run, or <c>null</c> while it is switched off.</summary>
    /// <returns>The first occurrence strictly after the anchor, or <c>null</c> for a disabled schedule.</returns>
    /// <remarks>
    /// <para>
    /// <b>The anchor is the later of <see cref="LastRunAt"/> and <see cref="EnabledAt"/>.</b> The last
    /// run alone would make a schedule switched off and on again due at once, because its run was
    /// before the switch and the hour has passed since; the switch-on alone would forget the run.
    /// The later of the two is the instant from which nothing has yet been owed.
    /// </para>
    /// <para>
    /// The answer can lie in the past: a panel that was down across the hour still owes that run,
    /// and <see cref="IsDue"/> takes it at the next tick.
    /// </para>
    /// </remarks>
    public DateTimeOffset? NextRunAt()
    {
        if (!Enabled || EnabledAt is null)
        {
            return null;
        }

        var anchor = LastRunAt is not null && LastRunAt.Value > EnabledAt.Value
            ? LastRunAt.Value
            : EnabledAt.Value;

        return MostRecentOccurrence(anchor).Add(Period());
    }
    /// <summary>The time between two occurrences of this schedule.</summary>
    /// <returns>A week for a weekly schedule with a weekday; a day otherwise.</returns>
    /// <remarks>
    /// The same reading <see cref="MostRecentOccurrence"/> makes: a weekly schedule with no weekday
    /// is treated as daily, so the two can never disagree about which occurrence follows which.
    /// </remarks>
    private TimeSpan Period()
    {
        if (Frequency == BackupFrequency.Daily || DayOfWeekUtc is null)
        {
            return TimeSpan.FromDays(1);
        }

        return TimeSpan.FromDays(7);
    }

    /// <summary>The latest instant this schedule was supposed to run, at or before <paramref name="now"/>.</summary>
    /// <param name="now">The instant to measure back from.</param>
    /// <returns>The occurrence, always before or exactly at <paramref name="now"/>.</returns>
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
