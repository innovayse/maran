using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.ValueObjects;
using Maran.Modules.Backups.Queries.GetMyBackupSchedule;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Backups.Tests.Queries.GetMyBackupSchedule;

/// <summary>Reading the caller's own schedule: only theirs, and nothing invented when there is none.</summary>
public sealed class GetMyBackupScheduleQueryHandlerTests
{
    /// <summary>The caller's own schedule is read, not the host policy and not a neighbour's.</summary>
    [Fact]
    public async Task The_callers_own_schedule_is_read_and_no_other()
    {
        var own = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using (var seed = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.BackupSchedules.Add(new BackupSchedule(Guid.NewGuid(), null, null, new DailyBackupCadence(1), 30));
            seed.BackupSchedules.Add(new BackupSchedule(Guid.NewGuid(), stranger, null, new DailyBackupCadence(2), 4));
            seed.BackupSchedules.Add(new BackupSchedule(Guid.NewGuid(), own, null, new DailyBackupCadence(9), 3));
            await seed.SaveChangesAsync();
        }

        var handler = Handler(database, own);

        var result = await handler.HandleAsync(new GetMyBackupScheduleQuery(own), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Schedule);
        Assert.Equal(own, result.Value.Schedule.AccountId);
        Assert.Equal(9, result.Value.Schedule.HourUtc);
    }

    /// <summary>An account with no schedule is answered with an empty scope, not the host policy.</summary>
    [Fact]
    public async Task An_account_with_no_schedule_is_answered_with_an_empty_scope()
    {
        var own = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using (var seed = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.BackupSchedules.Add(new BackupSchedule(Guid.NewGuid(), null, null, new DailyBackupCadence(1), 30));
            await seed.SaveChangesAsync();
        }

        var result = await Handler(database, own).HandleAsync(new GetMyBackupScheduleQuery(own), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Schedule);
    }

    /// <summary>An account the caller does not own is answered not found, whatever is stored for it.</summary>
    [Fact]
    public async Task An_account_the_caller_does_not_own_is_answered_not_found()
    {
        var own = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using (var seed = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.BackupSchedules.Add(new BackupSchedule(Guid.NewGuid(), stranger, null, new DailyBackupCadence(2), 4));
            await seed.SaveChangesAsync();
        }

        var result = await Handler(database, own).HandleAsync(new GetMyBackupScheduleQuery(stranger), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    /// <summary>Builds the handler for a customer of the given account.</summary>
    /// <param name="database">The in-memory database name.</param>
    /// <param name="ownAccount">The only account the directory shows the caller.</param>
    /// <returns>The handler.</returns>
    private static GetMyBackupScheduleQueryHandler Handler(string database, Guid ownAccount)
    {
        return new GetMyBackupScheduleQueryHandler(
            BackupsTestContext.Create(FakeCurrentUser.Customer(ownAccount), database),
            new StubAccountDirectory(
                new AccountSnapshot(ownAccount, "owner01", 1, 1, 1, 1, 1, 1024, AccountState.Active)));
    }
}
