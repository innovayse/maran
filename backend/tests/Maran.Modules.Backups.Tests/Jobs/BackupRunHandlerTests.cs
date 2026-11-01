using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Jobs;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Jobs;

/// <summary>
/// The nightly sweep: which schedules it runs, what it writes, when it asks for retention, and the
/// property the five-minute cadence rests on — a due schedule produces one backup, not one per tick.
/// </summary>
public sealed class BackupRunHandlerTests
{
    /// <summary>The system user name the directory reports for the account.</summary>
    private const string Username = BackupSweepWorld.Username;

    /// <summary>The instant the schedule was switched on: a Thursday at 04:00 UTC.</summary>
    private static readonly DateTimeOffset EnabledAt = BackupSweepWorld.EnabledAt;

    /// <summary>The schedule's first named hour after it was switched on: the next day at 03:00.</summary>
    private static readonly DateTimeOffset NamedHour = BackupSweepWorld.NamedHour;

    /// <summary>The first five-minute tick past the schedule's 03:00 hour — on time, so nothing was missed.</summary>
    private static readonly DateTimeOffset FirstTick = NamedHour.AddMinutes(1);

    /// <summary>A terminal event describing a finished archive.</summary>
    private static readonly BackupCreateEvent Created =
        new(BackupCreateEventKind.Created, 100, "done", 4096, new string('b', 64), 3, null);

    /// <summary>A terminal event describing a run the agent could not finish.</summary>
    private static readonly BackupCreateEvent Dropped =
        new(BackupCreateEventKind.Dropped, 40, "archiving", 0, string.Empty, 0, null);

    /// <summary>A due host-wide schedule backs up every account, as a scheduled backup.</summary>
    /// <remarks>
    /// This is the only place <see cref="BackupKind.Scheduled"/> is ever written, and the kind is the
    /// thing retention reads — so the assertion on it is not decoration.
    /// </remarks>
    [Fact]
    public async Task A_due_host_wide_schedule_backs_up_every_account_as_a_scheduled_backup()
    {
        var accountId = Guid.NewGuid();
        var world = await BackupSweepWorld.WithScheduleAsync(accountId, null, FirstTick, [Created]);

        var started = await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Equal(1, started);

        var call = Assert.Single(world.Agent.Creates);
        Assert.Equal(Username, call.AccountUsername);

        var row = Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(BackupKind.Scheduled, row.Kind);
        Assert.Equal(BackupStatus.Completed, row.Status);
        Assert.Equal(accountId, row.AccountId);
    }

    /// <summary>A due schedule fires once and only once across two cadence ticks.</summary>
    /// <remarks>
    /// The scheduler publishes every five minutes, so this is the proposition that keeps one night
    /// from producing twelve archives of every account on the host. The second sweep is a second
    /// handler over the SAME database with a later clock, which is what a second tick is.
    /// </remarks>
    [Fact]
    public async Task A_due_schedule_fires_once_and_only_once_across_two_ticks()
    {
        var accountId = Guid.NewGuid();
        var world = await BackupSweepWorld.WithScheduleAsync(accountId, null, FirstTick, [Created]);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);
        await world.At(FirstTick.AddMinutes(5)).HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Single(world.Agent.Creates);
        Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>A tick just after the schedule was switched on starts nothing and records no run.</summary>
    /// <remarks>
    /// Issue #224. The schedule names 03:00 and was switched on at 04:00, so 03:00 today has passed
    /// but was never owed: the first run is tomorrow's. A rule measuring from "never ran" would take
    /// a backup here, five minutes after the save.
    /// </remarks>
    [Fact]
    public async Task A_tick_just_after_the_save_starts_nothing()
    {
        var accountId = Guid.NewGuid();
        var world = await BackupSweepWorld.WithScheduleAsync(accountId, null, EnabledAt.AddMinutes(5), [Created]);

        var started = await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Equal(0, started);
        Assert.Empty(world.Agent.Creates);
        Assert.Null(world.Read().BackupSchedules.IgnoreQueryFilters().Single().LastRunAt);
    }

    /// <summary>Ticks from the save through the named hour start exactly one run, at that hour.</summary>
    /// <remarks>
    /// The whole first day as the five-minute scheduler sees it: ticks before 03:00 find nothing
    /// owed, the first tick past it starts one backup and stamps the run, and the tick after that
    /// finds it already taken.
    /// </remarks>
    [Fact]
    public async Task Ticks_from_the_save_through_the_named_hour_start_exactly_one_run()
    {
        var accountId = Guid.NewGuid();
        var world = await BackupSweepWorld.WithScheduleAsync(accountId, null, EnabledAt.AddMinutes(5), [Created]);

        var beforeTheHour = await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);
        var lastTickBeforeTheHour = await world.At(NamedHour.AddMinutes(-5))
            .HandleAsync(new BackupRunRequested(), CancellationToken.None);
        var atTheHour = await world.At(NamedHour).HandleAsync(new BackupRunRequested(), CancellationToken.None);
        var afterTheHour = await world.At(NamedHour.AddMinutes(5))
            .HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Equal([0, 0, 1, 0], [beforeTheHour, lastTickBeforeTheHour, atTheHour, afterTheHour]);
        Assert.Single(world.Agent.Creates);
        Assert.Equal(NamedHour, world.Read().BackupSchedules.IgnoreQueryFilters().Single().LastRunAt);
    }

    /// <summary>The run stamp is written before the agent is asked for anything.</summary>
    /// <remarks>
    /// Observed at the moment the agent is called rather than after the sweep, because "it is set
    /// afterwards" and "it is set beforehand" are indistinguishable once the sweep has returned —
    /// and the whole point is what a tick five minutes into a running backup would see. The
    /// mutation that moves <c>MarkRunStarted</c> after the run must fail here.
    /// </remarks>
    [Fact]
    public async Task The_run_stamp_is_written_before_the_agent_is_asked()
    {
        var accountId = Guid.NewGuid();
        var world = await BackupSweepWorld.WithScheduleAsync(accountId, null, FirstTick, [Created]);

        DateTimeOffset? stampWhenTheAgentWasCalled = null;
        world.Agent.OnCreate = () =>
        {
            stampWhenTheAgentWasCalled = world.Read().BackupSchedules
                .IgnoreQueryFilters()
                .Single()
                .LastRunAt;
        };

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Equal(FirstTick, stampWhenTheAgentWasCalled);
    }

    /// <summary>A disabled schedule never fires.</summary>
    [Fact]
    public async Task A_disabled_schedule_never_fires()
    {
        var accountId = Guid.NewGuid();
        var world = await BackupSweepWorld.WithScheduleAsync(accountId, null, FirstTick, [Created], enabled: false);

        var started = await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Equal(0, started);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>A completed run asks for retention with the schedule's retained count.</summary>
    [Fact]
    public async Task A_completed_run_asks_for_retention()
    {
        var accountId = Guid.NewGuid();
        var world = await BackupSweepWorld.WithScheduleAsync(accountId, null, FirstTick, [Created]);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        var request = Assert.IsType<RetentionRequested>(Assert.Single(world.Bus.Published));
        Assert.Equal(accountId, request.AccountId);
        Assert.Equal(BackupSweepWorld.RetainCount, request.RetainCount);
    }

    /// <summary>A failed run prunes nothing.</summary>
    /// <remarks>
    /// R12, and the reason it is a rule: pruning after a failure means one bad night costs the
    /// oldest copy that still worked, and a run fails precisely on the nights when the old copies
    /// matter. The mutation that publishes the retention request unconditionally must fail here.
    /// </remarks>
    [Fact]
    public async Task A_failed_run_prunes_nothing()
    {
        var accountId = Guid.NewGuid();
        var world = await BackupSweepWorld.WithScheduleAsync(accountId, null, FirstTick, [Dropped]);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.DoesNotContain(world.Bus.Published, message => { return message is RetentionRequested; });

        var row = Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(BackupStatus.Failed, row.Status);

        var entry = Assert.Single(
            world.Audit.Entries, entry => { return entry.Action == AuditActions.BackupCreated; });
        Assert.Equal(AuditActions.BackupCreated, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>An unattended run is journalled as the panel, not as an anonymous caller.</summary>
    /// <remarks>
    /// Outside a request there is no principal, so an ordinary entry would record an empty actor —
    /// exactly what a failed anonymous probe records, and an operator could not tell the two apart.
    /// </remarks>
    [Fact]
    public async Task An_unattended_run_is_journalled_as_the_panel()
    {
        var accountId = Guid.NewGuid();
        var world = await BackupSweepWorld.WithScheduleAsync(accountId, null, FirstTick, [Created]);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Null(entry.ActorUserId);
        Assert.Equal(SystemAuditEntry.NameFor(BackupAuditJournal.ModuleName), entry.ActorUsername);
        Assert.Equal(string.Empty, entry.IpAddress);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An account override backs up only that account.</summary>
    /// <remarks>
    /// The inverse control for the host-wide case: a <c>Targets</c> mutated to ignore the schedule's
    /// account passes every test above and fails this one.
    /// </remarks>
    [Fact]
    public async Task An_account_override_backs_up_only_that_account()
    {
        var chosen = Guid.NewGuid();
        var other = Guid.NewGuid();
        var world = await BackupSweepWorld.WithScheduleAsync(
            chosen, chosen, FirstTick, [Created], secondAccountId: other);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        var row = Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(chosen, row.AccountId);
    }
}
