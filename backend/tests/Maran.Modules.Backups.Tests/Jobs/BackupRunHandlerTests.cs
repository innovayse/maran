using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Jobs;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Backups.Tests.Jobs;

/// <summary>
/// The nightly sweep: which schedules it runs, what it writes, when it asks for retention, and the
/// property the five-minute cadence rests on — a due schedule produces one backup, not one per tick.
/// </summary>
public sealed class BackupRunHandlerTests
{
    /// <summary>The system user name the directory reports for the account.</summary>
    private const string Username = "cust01";

    /// <summary>The instant the schedule was switched on: a Thursday at 04:00 UTC.</summary>
    private static readonly DateTimeOffset EnabledAt = new(2026, 1, 1, 4, 0, 0, TimeSpan.Zero);

    /// <summary>The following day, just past the schedule's 03:00 hour.</summary>
    private static readonly DateTimeOffset FirstTick = EnabledAt.AddDays(1).AddMinutes(1);

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
        var world = await World.WithScheduleAsync(accountId, null, FirstTick, [Created]);

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
        var world = await World.WithScheduleAsync(accountId, null, FirstTick, [Created]);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);
        await world.At(FirstTick.AddMinutes(5)).HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Single(world.Agent.Creates);
        Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
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
        var world = await World.WithScheduleAsync(accountId, null, FirstTick, [Created]);

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
        var world = await World.WithScheduleAsync(accountId, null, FirstTick, [Created], enabled: false);

        var started = await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Equal(0, started);
        Assert.Empty(world.Agent.Creates);
    }

    /// <summary>A completed run asks for retention with the schedule's retained count.</summary>
    [Fact]
    public async Task A_completed_run_asks_for_retention()
    {
        var accountId = Guid.NewGuid();
        var world = await World.WithScheduleAsync(accountId, null, FirstTick, [Created]);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        var request = Assert.IsType<RetentionRequested>(Assert.Single(world.Bus.Published));
        Assert.Equal(accountId, request.AccountId);
        Assert.Equal(World.RetainCount, request.RetainCount);
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
        var world = await World.WithScheduleAsync(accountId, null, FirstTick, [Dropped]);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Empty(world.Bus.Published);

        var row = Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(BackupStatus.Failed, row.Status);

        var entry = Assert.Single(world.Audit.Entries);
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
        var world = await World.WithScheduleAsync(accountId, null, FirstTick, [Created]);

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
        var world = await World.WithScheduleAsync(chosen, chosen, FirstTick, [Created], secondAccountId: other);

        await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        var row = Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(chosen, row.AccountId);
    }

    /// <summary>The doubles, the database and the handler, assembled once per test.</summary>
    private sealed class World
    {
        /// <summary>How many backups the seeded schedule keeps.</summary>
        public const int RetainCount = 3;

        /// <summary>The in-memory database the handler and the reader share.</summary>
        private readonly string _database;

        /// <summary>The principal both contexts are bound to; an administrator sees every row.</summary>
        private readonly FakeCurrentUser _currentUser;

        /// <summary>The accounts the directory knows.</summary>
        private readonly StubAccountDirectory _accounts;

        /// <summary>The handler under test, at the sweep's own instant.</summary>
        public BackupRunHandler Handler { get; private set; } = null!;

        /// <summary>
        /// The agent double, holding what it was asked for. Built ONCE and shared by every tick, so
        /// a test asking "how many archives did two ticks produce" reads one list rather than two.
        /// </summary>
        public StubAgentBackupClient Agent { get; }

        /// <summary>The audit double, holding what was journalled.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>The bus double, holding the retention requests the sweep published.</summary>
        public RecordingMessageBus Bus { get; } = new();

        /// <summary>Assembles the world.</summary>
        /// <param name="accountId">The account the directory knows and the schedule may name.</param>
        /// <param name="secondAccountId">A second account the directory knows, or <c>null</c>.</param>
        /// <param name="events">The create stream the agent replays.</param>
        private World(Guid accountId, Guid? secondAccountId, IReadOnlyList<BackupCreateEvent> events)
        {
            _database = Guid.NewGuid().ToString();
            _currentUser = FakeCurrentUser.Admin();
            Agent = new StubAgentBackupClient(events);

            var snapshots = new List<AccountSnapshot>
            {
                new(accountId, Username, 1, 1, 1, 1, 1, 1024),
            };

            if (secondAccountId is not null)
            {
                snapshots.Add(new AccountSnapshot(secondAccountId.Value, "cust02", 1, 1, 1, 1, 1, 1024));
            }

            _accounts = new StubAccountDirectory([.. snapshots]);
        }

        /// <summary>Seeds a schedule and assembles the handler at <paramref name="now"/>.</summary>
        /// <param name="accountId">The account the directory knows.</param>
        /// <param name="scheduleAccountId">The account the schedule names, or <c>null</c> for host-wide.</param>
        /// <param name="now">The instant the sweep runs at.</param>
        /// <param name="events">The create stream the agent replays.</param>
        /// <param name="enabled">Whether the schedule is switched on.</param>
        /// <param name="secondAccountId">A second account the directory knows, or <c>null</c>.</param>
        /// <returns>The assembled world.</returns>
        public static async Task<World> WithScheduleAsync(
            Guid accountId,
            Guid? scheduleAccountId,
            DateTimeOffset now,
            IReadOnlyList<BackupCreateEvent> events,
            bool enabled = true,
            Guid? secondAccountId = null)
        {
            var world = new World(accountId, secondAccountId, events);

            var schedule = new BackupSchedule(
                Guid.NewGuid(), scheduleAccountId, null, BackupFrequency.Daily, 3, null, RetainCount);
            schedule.Reconfigure(null, BackupFrequency.Daily, 3, null, RetainCount, enabled, EnabledAt);

            await using var seed = BackupsTestContext.Create(world._currentUser, world._database);
            seed.BackupSchedules.Add(schedule);
            await seed.SaveChangesAsync();

            world.At(now);

            return world;
        }

        /// <summary>Rebuilds the handler at a later instant — which is what a later tick is.</summary>
        /// <param name="now">The instant the next sweep runs at.</param>
        /// <returns>The handler bound to that instant and the same database.</returns>
        public BackupRunHandler At(DateTimeOffset now)
        {
            var tasks = new RecordingTaskRecorder();

            var dbContext = BackupsTestContext.CreateSeeded(_currentUser, _database);

            Handler = new BackupRunHandler(
                dbContext,
                _accounts,
                new BackupRunner(Agent, tasks),
                new BackupDestinationResolver(dbContext),
                new BackupAuditJournal(Audit, _currentUser),
                new FakeClock(now),
                tasks,
                new StubCorrelationIdAccessor("corr-sweep"),
                Bus,
                NullLogger<BackupRunHandler>.Instance);

            return Handler;
        }

        /// <summary>Opens a context over the same database, to read what the sweep wrote.</summary>
        /// <returns>A context bound to the same principal and database.</returns>
        public BackupsDbContext Read()
        {
            return BackupsTestContext.Create(_currentUser, _database);
        }
    }
}
