using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;

namespace Maran.Modules.Backups.Tests.Domain;

/// <summary>
/// When a schedule is due, when it is not, and the one property everything else rests on: that a
/// schedule fires once for each occurrence and not once per five-minute tick.
/// </summary>
public sealed class BackupScheduleTests
{
    /// <summary>A Thursday, so a weekly schedule's weekday arithmetic has somewhere to be wrong.</summary>
    private static readonly DateTimeOffset Thursday0400 = new(2026, 1, 1, 4, 0, 0, TimeSpan.Zero);

    /// <summary>The first 03:00 after <see cref="Thursday0400"/>.</summary>
    private static readonly DateTimeOffset Friday0300 = new(2026, 1, 2, 3, 0, 0, TimeSpan.Zero);

    /// <summary>A new schedule is disabled, whatever it was created with.</summary>
    /// <remarks>
    /// The off-by-default promise, held by the constructor rather than by whoever calls it: a
    /// schedule the operator did not switch on cannot start writing archives.
    /// </remarks>
    [Fact]
    public void A_new_schedule_is_disabled()
    {
        var schedule = Daily();

        Assert.False(schedule.Enabled);
        Assert.Null(schedule.EnabledAt);
        Assert.Null(schedule.LastRunAt);
        Assert.Null(schedule.NextRunAt());
        Assert.False(schedule.IsDue(Thursday0400));
    }

    /// <summary>A disabled schedule is never due, however long ago its hour passed.</summary>
    [Fact]
    public void A_disabled_schedule_is_never_due()
    {
        var schedule = Daily();
        schedule.MarkRunStarted(Thursday0400.AddYears(-1));

        Assert.False(schedule.IsDue(Thursday0400));
    }

    /// <summary>Enabling a schedule records when, runs nothing, and waits for the next named hour.</summary>
    /// <remarks>
    /// Issue #224: the switch-on used to be written into <c>LastRunAt</c>, so the screen showed a form
    /// save as a run. It is its own fact now, <c>LastRunAt</c> stays empty until a real run, and the
    /// first run is the next 03:00 — not the save, and not 03:00 today, which has already passed.
    /// </remarks>
    [Fact]
    public void Enabling_a_schedule_does_not_make_it_due_at_once()
    {
        var schedule = Daily();
        schedule.Reconfigure(null, BackupFrequency.Daily, 3, null, 7, enabled: true, Thursday0400);

        Assert.Equal(Thursday0400, schedule.EnabledAt);
        Assert.Null(schedule.LastRunAt);
        Assert.Equal(Friday0300, schedule.NextRunAt());
        Assert.False(schedule.IsDue(Thursday0400));
        Assert.False(schedule.IsDue(Friday0300.AddMinutes(-1)));
        Assert.True(schedule.IsDue(Friday0300));
    }

    /// <summary>Re-saving an already enabled schedule does not postpone its next run.</summary>
    /// <remarks>
    /// The other half of the switch-on rule. If every save moved <c>EnabledAt</c>, a form saved once
    /// a day would postpone the backup for ever and nothing on the screen would say so.
    /// </remarks>
    [Fact]
    public void Re_saving_an_enabled_schedule_does_not_postpone_its_next_run()
    {
        var schedule = Enabled(3);
        var afterTheHour = Thursday0400.AddDays(1);

        schedule.Reconfigure(null, BackupFrequency.Daily, 3, null, 7, enabled: true, afterTheHour);

        Assert.Equal(Thursday0400, schedule.EnabledAt);
        Assert.Null(schedule.LastRunAt);
        Assert.True(schedule.IsDue(afterTheHour));
    }

    /// <summary>Switching a schedule off clears when it was enabled, and it has no next run.</summary>
    [Fact]
    public void Switching_a_schedule_off_clears_its_enabled_instant_and_next_run()
    {
        var schedule = Enabled(3);

        schedule.Reconfigure(null, BackupFrequency.Daily, 3, null, 7, enabled: false, Thursday0400.AddHours(1));

        Assert.Null(schedule.EnabledAt);
        Assert.Null(schedule.NextRunAt());
        Assert.False(schedule.IsDue(Thursday0400.AddDays(3)));
    }

    /// <summary>Switching a schedule that has run off and on again does not make it due at once.</summary>
    /// <remarks>
    /// Why the anchor is the LATER of the last run and the switch-on, not the last run alone: the run
    /// was days before the switch-on and several 03:00s have passed since it, so measuring from the run
    /// would start a backup within five minutes of the save — the very surprise #224 is about.
    /// </remarks>
    [Fact]
    public void Switching_a_schedule_off_and_on_again_does_not_make_it_due_at_once()
    {
        var schedule = Enabled(3);
        schedule.MarkRunStarted(Friday0300);
        schedule.Reconfigure(null, BackupFrequency.Daily, 3, null, 7, enabled: false, Friday0300.AddHours(2));

        var switchedOnAgain = Friday0300.AddDays(3).AddHours(5);
        schedule.Reconfigure(null, BackupFrequency.Daily, 3, null, 7, enabled: true, switchedOnAgain);

        Assert.Equal(Friday0300, schedule.LastRunAt);
        Assert.Equal(switchedOnAgain, schedule.EnabledAt);
        Assert.False(schedule.IsDue(switchedOnAgain));
        Assert.Equal(Friday0300.AddDays(4), schedule.NextRunAt());
    }

    /// <summary>A due schedule fires once and only once across two cadence ticks.</summary>
    /// <remarks>
    /// The proposition the whole design exists for. The scheduler ticks every five minutes, so a
    /// rule that answered "the hour matches" would fire twelve times a night; the comparison against
    /// the occurrence plus the run stamp is what makes the second tick answer no.
    /// </remarks>
    [Fact]
    public void A_due_schedule_fires_once_and_only_once_across_two_ticks()
    {
        var schedule = Enabled(3);
        var firstTick = Thursday0400.AddDays(1).AddMinutes(1);
        var secondTick = firstTick.AddMinutes(5);

        Assert.True(schedule.IsDue(firstTick));

        schedule.MarkRunStarted(firstTick);

        Assert.Equal(firstTick, schedule.LastRunAt);
        Assert.Equal(Friday0300.AddDays(1), schedule.NextRunAt());
        Assert.False(schedule.IsDue(secondTick));
        Assert.False(schedule.IsDue(secondTick.AddHours(20)));
    }

    /// <summary>The next day's occurrence makes the schedule due again.</summary>
    /// <remarks>
    /// The inverse control for the test above: a rule mutated to answer "never due once run" passes
    /// that one and fails this.
    /// </remarks>
    [Fact]
    public void The_next_days_occurrence_makes_the_schedule_due_again()
    {
        var schedule = Enabled(3);
        var firstRun = Thursday0400.AddDays(1).AddMinutes(1);
        schedule.MarkRunStarted(firstRun);

        Assert.True(schedule.IsDue(firstRun.AddDays(1)));
    }

    /// <summary>A panel that was switched off across the hour takes the backup when it returns.</summary>
    /// <remarks>
    /// A nightly backup missed because nobody was watching at 03:00 is not a nightly backup. The
    /// occurrence comparison gives this for nothing; an hour match could not express it at all.
    /// </remarks>
    [Fact]
    public void A_panel_that_missed_the_hour_takes_the_backup_when_it_returns()
    {
        var schedule = Enabled(3);

        // The owed run stays at the hour that was missed, so the next tick takes it.
        Assert.Equal(Friday0300, schedule.NextRunAt());
        Assert.True(schedule.IsDue(Thursday0400.AddDays(1).AddHours(9)));
    }

    /// <summary>A weekly schedule is not due on any day but its own.</summary>
    [Fact]
    public void A_weekly_schedule_is_not_due_on_another_day()
    {
        var schedule = new BackupSchedule(
            Guid.NewGuid(), null, null, BackupFrequency.Weekly, 3, DayOfWeek.Sunday, 7);
        schedule.Reconfigure(null, BackupFrequency.Weekly, 3, DayOfWeek.Sunday, 7, enabled: true, Thursday0400);

        // Friday, Saturday: the most recent Sunday occurrence is still behind the switch-on.
        Assert.False(schedule.IsDue(Thursday0400.AddDays(1)));
        Assert.False(schedule.IsDue(Thursday0400.AddDays(2)));

        // Sunday, after the hour.
        Assert.Equal(new DateTimeOffset(2026, 1, 4, 3, 0, 0, TimeSpan.Zero), schedule.NextRunAt());
        Assert.True(schedule.IsDue(Thursday0400.AddDays(3)));
    }

    /// <summary>An hour that has not arrived yet today does not fire.</summary>
    /// <remarks>
    /// Guards the "today or yesterday" arm of the occurrence: a schedule at 23:00 read at 04:00 must
    /// compare against last night's 23:00, which the switch-on already covers — not tonight's, which
    /// has not happened.
    /// </remarks>
    [Fact]
    public void An_hour_that_has_not_arrived_today_does_not_fire()
    {
        var schedule = Enabled(23);

        Assert.False(schedule.IsDue(Thursday0400.AddHours(2)));
        Assert.True(schedule.IsDue(Thursday0400.AddHours(20)));
    }

    /// <summary>Builds a disabled daily schedule at 03:00 keeping seven backups.</summary>
    /// <returns>The schedule.</returns>
    private static BackupSchedule Daily()
    {
        return new BackupSchedule(Guid.NewGuid(), null, null, BackupFrequency.Daily, 3, null, 7);
    }

    /// <summary>Builds a daily schedule enabled at <see cref="Thursday0400"/>.</summary>
    /// <param name="hourUtc">The hour the schedule runs at.</param>
    /// <returns>The schedule, switched on at the fixture instant.</returns>
    private static BackupSchedule Enabled(int hourUtc)
    {
        var schedule = new BackupSchedule(Guid.NewGuid(), null, null, BackupFrequency.Daily, hourUtc, null, 7);
        schedule.Reconfigure(null, BackupFrequency.Daily, hourUtc, null, 7, enabled: true, Thursday0400);

        return schedule;
    }
}
