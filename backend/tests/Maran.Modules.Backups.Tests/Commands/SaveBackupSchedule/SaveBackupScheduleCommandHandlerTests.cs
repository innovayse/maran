using Maran.Modules.Backups.Commands.SaveBackupSchedule;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Commands.SaveBackupSchedule;

/// <summary>
/// Saving a schedule: what a first save writes, what a second one replaces rather than duplicates,
/// and what a schedule for an account that is not there answers.
/// </summary>
public sealed class SaveBackupScheduleCommandHandlerTests
{
    /// <summary>The instant the handler's clock reports.</summary>
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 4, 0, 0, TimeSpan.Zero);

    /// <summary>A first save creates the host-wide schedule.</summary>
    [Fact]
    public async Task A_first_save_creates_the_host_wide_schedule()
    {
        var world = new World(Guid.NewGuid());

        var result = await world.Handler.HandleAsync(Command(null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.AccountId);
        Assert.True(result.Value.Enabled);
        Assert.Equal(BackupFrequency.Daily, result.Value.Frequency);
        Assert.Equal(3, result.Value.HourUtc);
        Assert.Equal(7, result.Value.RetainCount);

        // Stamped as switched on, which is what stops the first run happening within five minutes.
        Assert.Equal(Now, result.Value.LastRunAt);

        var stored = Assert.Single(await world.Read().BackupSchedules.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(result.Value.Id, stored.Id);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.BackupScheduleSaved, entry.Action);

        // Empty, not the schedule's row id: the host-wide schedule acts on no one account, and the
        // action already names what was saved. An account-scoped save records that account's name.
        Assert.Equal(string.Empty, entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A second save replaces the schedule rather than adding another.</summary>
    /// <remarks>
    /// The host-wide row is the one a unique index cannot hold single on every supported PostgreSQL,
    /// so this lookup is what does. Two host-wide schedules would take two backups of every account
    /// a night and neither of them would be wrong.
    /// </remarks>
    [Fact]
    public async Task A_second_save_replaces_the_schedule_rather_than_adding_another()
    {
        var world = new World(Guid.NewGuid());

        var first = await world.Handler.HandleAsync(Command(null), CancellationToken.None);
        var second = await world.Handler.HandleAsync(
            Command(null) with { HourUtc = 5, RetainCount = 30 }, CancellationToken.None);

        Assert.Equal(first.Value.Id, second.Value.Id);

        var stored = Assert.Single(await world.Read().BackupSchedules.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(5, stored.HourUtc);
        Assert.Equal(30, stored.RetainCount);
    }

    /// <summary>An account's own schedule is a different row from the host-wide one.</summary>
    /// <remarks>
    /// The inverse control for the test above: a lookup mutated to ignore the account would find the
    /// host-wide row and overwrite it, so this asserts two rows and not one.
    /// </remarks>
    [Fact]
    public async Task An_account_override_is_a_different_row_from_the_host_wide_one()
    {
        var accountId = Guid.NewGuid();
        var world = new World(accountId);

        await world.Handler.HandleAsync(Command(null), CancellationToken.None);
        await world.Handler.HandleAsync(Command(accountId), CancellationToken.None);

        var stored = await world.Read().BackupSchedules.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Contains(stored, row => { return row.AccountId == accountId; });
        Assert.Contains(stored, row => { return row.AccountId is null; });
    }

    /// <summary>A schedule for an account that is not there is refused, and nothing is written.</summary>
    /// <remarks>
    /// Not-found rather than a row nothing will ever run: the sweep would select it every night and
    /// find no account to back up, and no screen would ever say why.
    /// </remarks>
    [Fact]
    public async Task A_schedule_for_an_unknown_account_is_refused()
    {
        var world = new World(Guid.NewGuid());

        var result = await world.Handler.HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(await world.Read().BackupSchedules.IgnoreQueryFilters().ToListAsync());

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(AuditActions.BackupScheduleSaved, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>Builds the command under test.</summary>
    /// <param name="accountId">The account the schedule names, or <c>null</c> for host-wide.</param>
    /// <returns>An enabled daily schedule at 03:00 keeping seven backups.</returns>
    private static SaveBackupScheduleCommand Command(Guid? accountId)
    {
        return new SaveBackupScheduleCommand(
            accountId, null, BackupFrequency.Daily, 3, null, 7, Enabled: true, "10.0.0.1", "agent");
    }

    /// <summary>The doubles, the database and the handler, assembled once per test.</summary>
    private sealed class World
    {
        /// <summary>The in-memory database the handler and the reader share.</summary>
        private readonly string _database = Guid.NewGuid().ToString();

        /// <summary>The principal both contexts are bound to; the surface is administrators-only.</summary>
        private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Admin();

        /// <summary>The handler under test.</summary>
        public SaveBackupScheduleCommandHandler Handler { get; }

        /// <summary>The audit double, holding what was journalled.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>Assembles the handler over doubles.</summary>
        /// <param name="knownAccountId">The one account the directory knows.</param>
        public World(Guid knownAccountId)
        {
            Handler = new SaveBackupScheduleCommandHandler(
                BackupsTestContext.Create(_currentUser, _database),
                new StubAccountDirectory(new AccountSnapshot(knownAccountId, "cust01", 1, 1, 1, 1, 1, 1024)),
                new BackupAuditJournal(Audit, _currentUser),
                new FakeClock(Now));
        }

        /// <summary>Opens a context over the same database, to read what the handler wrote.</summary>
        /// <returns>A context bound to the same principal and database.</returns>
        public BackupsDbContext Read()
        {
            return BackupsTestContext.Create(_currentUser, _database);
        }
    }
}
