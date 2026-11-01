using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Jobs;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Backups.Tests.TestSupport;

/// <summary>The doubles, the database and the handler, assembled once per test.</summary>
public sealed class BackupSweepWorld
{
    /// <summary>How many backups the seeded schedule keeps.</summary>
    public const int RetainCount = 3;

    /// <summary>The system user name the directory reports for the account.</summary>
    public const string Username = "cust01";

    /// <summary>Where the stub directory says operator alerts go.</summary>
    public const string AlertRecipient = "ops@example.test";

    /// <summary>The instant the schedule was switched on: a Thursday at 04:00 UTC.</summary>
    public static readonly DateTimeOffset EnabledAt = new(2026, 1, 1, 4, 0, 0, TimeSpan.Zero);

    /// <summary>The schedule's first named hour after it was switched on: the next day at 03:00.</summary>
    public static readonly DateTimeOffset NamedHour = new(2026, 1, 2, 3, 0, 0, TimeSpan.Zero);

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

    /// <summary>The bus double, holding the retention requests and alert mails the sweep published.</summary>
    public RecordingMessageBus Bus { get; } = new();

    /// <summary>
    /// The alert-recipient double. A test modelling a panel with no alert address replaces it and
    /// calls <see cref="At"/> again, which rebuilds the handler over the replacement.
    /// </summary>
    public StubAlertRecipientDirectory Recipients { get; set; } = new(AlertRecipient);

    /// <summary>Assembles the world.</summary>
    /// <param name="accountId">The account the directory knows and the schedule may name.</param>
    /// <param name="secondAccountId">A second account the directory knows, or <c>null</c>.</param>
    /// <param name="events">The create stream the agent replays.</param>
    private BackupSweepWorld(Guid accountId, Guid? secondAccountId, IReadOnlyList<BackupCreateEvent> events)
    {
        _database = Guid.NewGuid().ToString();
        _currentUser = FakeCurrentUser.Admin();
        Agent = new StubAgentBackupClient(events);

        var snapshots = new List<AccountSnapshot>
        {
            new(accountId, Username, 1, 1, 1, 1, 1, 1024, AccountState.Active),
        };

        if (secondAccountId is not null)
        {
            snapshots.Add(new AccountSnapshot(
                secondAccountId.Value, "cust02", 1, 1, 1, 1, 1, 1024, AccountState.Active));
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
    /// <param name="destinationId">The destination the schedule names, or <c>null</c> for the default.</param>
    /// <returns>The assembled world.</returns>
    public static async Task<BackupSweepWorld> WithScheduleAsync(
        Guid accountId,
        Guid? scheduleAccountId,
        DateTimeOffset now,
        IReadOnlyList<BackupCreateEvent> events,
        bool enabled = true,
        Guid? secondAccountId = null,
        Guid? destinationId = null)
    {
        var world = new BackupSweepWorld(accountId, secondAccountId, events);

        var schedule = new BackupSchedule(
            Guid.NewGuid(), scheduleAccountId, destinationId, BackupFrequency.Daily, 3, null, RetainCount);
        schedule.Reconfigure(destinationId, BackupFrequency.Daily, 3, null, RetainCount, enabled, EnabledAt);

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
            new BackupAlertMailer(
                Recipients,
                Bus,
                new BackupAuditJournal(Audit, _currentUser),
                BackupsTestContext.AlertText(),
                BackupsTestContext.FailureNames()),
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
