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
        Assert.Null(schedule.LastRunAt);
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

    /// <summary>Enabling a schedule does not make it due at once.</summary>
    /// <remarks>
    /// The stamp inside <c>Reconfigure</c> is the whole of this. Without it, an operator switching a
    /// 03:00 schedule on at four in the afternoon would get a backup within five minutes, because
    /// 03:00 today is in the past and the schedule has never run.
    /// </remarks>
    [Fact]
    public void Enabling_a_schedule_does_not_make_it_due_at_once()
    {
        var schedule = Daily();
        schedule.Reconfigure(null, BackupFrequency.Daily, 3, null, 7, enabled: true, Thursday0400);

        Assert.Equal(Thursday0400, schedule.LastRunAt);
        Assert.False(schedule.IsDue(Thursday0400));
    }

    /// <summary>Re-saving an already enabled schedule does not postpone its next run.</summary>
    /// <remarks>
    /// The other half of the stamp rule. If every save re-stamped, a form saved once a day would
    /// postpone the backup for ever and nothing on the screen would say so.
    /// </remarks>
    [Fact]
    public void Re_saving_an_enabled_schedule_does_not_postpone_its_next_run()
    {
        var schedule = Enabled(3);
        var afterTheHour = Thursday0400.AddDays(1);

        schedule.Reconfigure(null, BackupFrequency.Daily, 3, null, 7, enabled: true, afterTheHour);

        Assert.Equal(Thursday0400, schedule.LastRunAt);
        Assert.True(schedule.IsDue(afterTheHour));
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

        Assert.True(schedule.IsDue(Thursday0400.AddDays(1).AddHours(9)));
    }

    /// <summary>A weekly schedule is not due on any day but its own.</summary>
    [Fact]
    public void A_weekly_schedule_is_not_due_on_another_day()
    {
        var schedule = new BackupSchedule(
            Guid.NewGuid(), null, null, BackupFrequency.Weekly, 3, DayOfWeek.Sunday, 7);
        schedule.Reconfigure(null, BackupFrequency.Weekly, 3, DayOfWeek.Sunday, 7, enabled: true, Thursday0400);

        // Friday, Saturday: the Sunday occurrence is still four and three days behind the stamp.
        Assert.False(schedule.IsDue(Thursday0400.AddDays(1)));
        Assert.False(schedule.IsDue(Thursday0400.AddDays(2)));

        // Sunday, after the hour.
        Assert.True(schedule.IsDue(Thursday0400.AddDays(3)));
    }

    /// <summary>An hour that has not arrived yet today does not fire.</summary>
    /// <remarks>
    /// Guards the "today or yesterday" arm of the occurrence: a schedule at 23:00 read at 04:00 must
    /// compare against last night's 23:00, which the stamp already covers — not tonight's, which has
    /// not happened.
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
    /// <returns>The schedule, stamped as switched on at the fixture instant.</returns>
    private static BackupSchedule Enabled(int hourUtc)
    {
        var schedule = new BackupSchedule(Guid.NewGuid(), null, null, BackupFrequency.Daily, hourUtc, null, 7);
        schedule.Reconfigure(null, BackupFrequency.Daily, hourUtc, null, 7, enabled: true, Thursday0400);

        return schedule;
    }
}
