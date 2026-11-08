using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Domain.ValueObjects;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Queries.CountConfiguredBackupDestinations;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Backups.Tests.Queries.CountConfiguredBackupDestinations;

/// <summary>Behavioral contract of <see cref="CountConfiguredBackupDestinationsQueryHandler"/>.</summary>
public sealed class CountConfiguredBackupDestinationsQueryHandlerTests
{
    /// <summary>
    /// A server holding only what the installation seeds — the local destination — counts zero.
    /// </summary>
    [Fact]
    public async Task The_seeded_local_destination_alone_counts_as_zero()
    {
        await using var dbContext = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());

        var result = await Count(dbContext);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
    }

    /// <summary>
    /// A destination the administrator added counts, beside the seeded one, which still does not: the
    /// inverse control that the query is not simply returning zero.
    /// </summary>
    [Fact]
    public async Task A_destination_the_administrator_added_counts_and_the_seeded_one_does_not()
    {
        await using var dbContext = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());
        dbContext.BackupDestinations.Add(new BackupDestination(
            Guid.NewGuid(),
            "Offsite",
            BackupDestinationKind.S3,
            "bucket/path",
            isDefault: false,
            BackupsTestContext.SeedInstant));
        await dbContext.SaveChangesAsync();

        var result = await Count(dbContext);

        Assert.Equal(1, result.Value);
    }

    /// <summary>A host-wide schedule that is switched on counts.</summary>
    [Fact]
    public async Task An_enabled_host_wide_schedule_counts()
    {
        await using var dbContext = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());
        dbContext.BackupSchedules.Add(Schedule(accountId: null, enabled: true));
        await dbContext.SaveChangesAsync();

        var result = await Count(dbContext);

        Assert.Equal(1, result.Value);
    }

    /// <summary>A schedule saved switched off backs nothing up and does not count.</summary>
    [Fact]
    public async Task A_schedule_that_is_switched_off_does_not_count()
    {
        await using var dbContext = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());
        dbContext.BackupSchedules.Add(Schedule(accountId: null, enabled: false));
        await dbContext.SaveChangesAsync();

        var result = await Count(dbContext);

        Assert.Equal(0, result.Value);
    }

    /// <summary>One account's own override is a customer's setting and does not answer the administrator's step.</summary>
    [Fact]
    public async Task An_accounts_own_schedule_does_not_count()
    {
        await using var dbContext = BackupsTestContext.CreateSeeded(FakeCurrentUser.Admin());
        dbContext.BackupSchedules.Add(Schedule(accountId: Guid.NewGuid(), enabled: true));
        await dbContext.SaveChangesAsync();

        var result = await Count(dbContext);

        Assert.Equal(0, result.Value);
    }

    /// <summary>Runs the handler against a context.</summary>
    /// <param name="dbContext">The context to count in.</param>
    /// <returns>The handler's result.</returns>
    private static Task<Result<int>> Count(BackupsDbContext dbContext)
    {
        return new CountConfiguredBackupDestinationsQueryHandler(dbContext)
            .HandleAsync(new CountConfiguredBackupDestinationsQuery(), CancellationToken.None);
    }

    /// <summary>Builds a daily schedule in the given state.</summary>
    /// <param name="accountId">The account it belongs to, or <c>null</c> for the host-wide one.</param>
    /// <param name="enabled">Whether it is switched on.</param>
    /// <returns>The schedule.</returns>
    private static BackupSchedule Schedule(Guid? accountId, bool enabled)
    {
        var schedule = new BackupSchedule(Guid.NewGuid(), accountId, null, new DailyBackupCadence(3), 7);
        schedule.Reconfigure(null, new DailyBackupCadence(3), 7, enabled, BackupsTestContext.SeedInstant);
        return schedule;
    }
}
