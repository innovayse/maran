using Maran.Modules.Backups.Commands.SaveMyBackupSchedule;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Domain.ValueObjects;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Services;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Commands.SaveMyBackupSchedule;

/// <summary>
/// Saving the caller's own schedule: what is written, for whom, and what is never touched (issue #147).
/// </summary>
public sealed class SaveMyBackupScheduleCommandHandlerTests
{
    /// <summary>The instant the handler's clock reports.</summary>
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 4, 0, 0, TimeSpan.Zero);

    /// <summary>A first save creates the caller's own schedule and journals it under their account.</summary>
    [Fact]
    public async Task A_first_save_creates_the_callers_own_schedule()
    {
        var world = new World();

        var result = await world.Handler.HandleAsync(Command(world.OwnAccountId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(world.OwnAccountId, result.Value.AccountId);
        Assert.Null(result.Value.DestinationId);
        Assert.True(result.Value.Enabled);
        Assert.Equal(5, result.Value.RetainCount);

        var stored = Assert.Single(await world.Read().BackupSchedules.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(world.OwnAccountId, stored.AccountId);

        var entry = Assert.Single(world.Audit.Entries);
        Assert.Equal(BackupAuditActions.BackupScheduleSaved, entry.Action);
        Assert.Equal("owner01", entry.Subject);
        Assert.Equal("10.0.0.1", entry.IpAddress);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A second save replaces the row rather than adding one.</summary>
    [Fact]
    public async Task A_second_save_replaces_the_row_rather_than_adding_another()
    {
        var world = new World();

        var first = await world.Handler.HandleAsync(Command(world.OwnAccountId), CancellationToken.None);
        var second = await world.Handler.HandleAsync(
            Command(world.OwnAccountId) with { HourUtc = 22, RetainCount = 2, Enabled = false },
            CancellationToken.None);

        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.False(second.Value.Enabled);
        var stored = Assert.Single(await world.Read().BackupSchedules.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(new DailyBackupCadence(22), stored.Cadence);
        Assert.Equal(2, stored.RetainCount);
    }

    /// <summary>The destination an administrator gave the row survives the customer's save.</summary>
    /// <remarks>
    /// The customer command carries no destination; a save that wrote <c>null</c> over the operator's
    /// choice would quietly repoint the account's nightly archive.
    /// </remarks>
    [Fact]
    public async Task The_destination_an_administrator_chose_survives_a_customers_save()
    {
        var world = new World();
        var destinationId = BackupDestination.DefaultDestinationId;
        await using (var seed = world.Read())
        {
            seed.BackupDestinations.Add(BackupsTestContext.DefaultDestinationRow());
            seed.BackupSchedules.Add(new BackupSchedule(
                Guid.NewGuid(), world.OwnAccountId, destinationId, new DailyBackupCadence(1), 3));
            await seed.SaveChangesAsync();
        }

        var result = await world.Handler.HandleAsync(Command(world.OwnAccountId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(destinationId, result.Value.DestinationId);
    }

    /// <summary>The host-wide policy is never touched, and an account the caller does not own writes nothing.</summary>
    /// <remarks>
    /// The refusing control for the tenant boundary: the stamped account is one the directory does not
    /// show this caller, so the answer is the same as for an account that does not exist.
    /// </remarks>
    [Fact]
    public async Task An_account_the_caller_does_not_own_is_answered_not_found_and_nothing_is_written()
    {
        var world = new World();
        await using (var seed = world.Read())
        {
            seed.BackupSchedules.Add(new BackupSchedule(Guid.NewGuid(), null, null, new DailyBackupCadence(1), 30));
            await seed.SaveChangesAsync();
        }

        var result = await world.Handler.HandleAsync(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);

        var stored = Assert.Single(await world.Read().BackupSchedules.IgnoreQueryFilters().ToListAsync());
        Assert.Null(stored.AccountId);
        Assert.Equal(30, stored.RetainCount);
        Assert.False(Assert.Single(world.Audit.Entries).Succeeded);
    }

    /// <summary>An identifier that names no account, which is what a non-customer caller is stamped with, is refused.</summary>
    [Fact]
    public async Task A_caller_with_no_account_of_their_own_is_answered_not_found()
    {
        var world = new World();

        var result = await world.Handler.HandleAsync(Command(Guid.Empty), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Empty(await world.Read().BackupSchedules.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>A weekly save naming no weekday is refused by the handler too, and nothing is written.</summary>
    [Fact]
    public async Task A_weekly_save_without_a_weekday_is_refused()
    {
        var world = new World();

        var result = await world.Handler.HandleAsync(
            Command(world.OwnAccountId) with { Frequency = BackupFrequency.Weekly }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BackupScheduleDayRequired", result.Error!.Code);
        Assert.Empty(await world.Read().BackupSchedules.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>Builds the command under test.</summary>
    /// <param name="accountId">The account the action stamped.</param>
    /// <returns>An enabled daily 03:00 schedule keeping five copies.</returns>
    private static SaveMyBackupScheduleCommand Command(Guid accountId)
    {
        return new SaveMyBackupScheduleCommand(
            BackupFrequency.Daily, 3, null, 5, Enabled: true, accountId, "10.0.0.1", "browser");
    }

    /// <summary>The doubles, the database and the handler, assembled once per test.</summary>
    private sealed class World
    {
        /// <summary>The in-memory database the handler and the reader share.</summary>
        private readonly string _database = Guid.NewGuid().ToString();

        /// <summary>The principal both contexts are bound to: a customer of the one known account.</summary>
        private readonly FakeCurrentUser _currentUser;

        /// <summary>The caller's own account.</summary>
        public Guid OwnAccountId { get; } = Guid.NewGuid();

        /// <summary>The handler under test.</summary>
        public SaveMyBackupScheduleCommandHandler Handler { get; }

        /// <summary>The audit double, holding what was journalled.</summary>
        public RecordingAuditWriter Audit { get; } = new();

        /// <summary>Assembles the handler over doubles.</summary>
        public World()
        {
            _currentUser = FakeCurrentUser.Customer(OwnAccountId);
            Handler = new SaveMyBackupScheduleCommandHandler(
                new StubAccountDirectory(
                    new AccountSnapshot(OwnAccountId, "owner01", 1, 1, 1, 1, 1, 1024, AccountState.Active)),
                new BackupAuditJournal(Audit, _currentUser),
                new BackupScheduleWriter(BackupsTestContext.Create(_currentUser, _database), new FakeClock(Now)));
        }

        /// <summary>Opens an administrator's context over the same database, to seed and read every row.</summary>
        /// <returns>A context the tenant filter does not narrow.</returns>
        public BackupsDbContext Read()
        {
            return BackupsTestContext.Create(FakeCurrentUser.Admin(), _database);
        }
    }
}
