using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Queries.GetBackupSchedule;
using Maran.Modules.Backups.Tests.TestSupport;

namespace Maran.Modules.Backups.Tests.Queries.GetBackupSchedule;

/// <summary>Reading a schedule, and what a server that has never configured one is told.</summary>
public sealed class GetBackupScheduleQueryHandlerTests
{
    /// <summary>The host-wide schedule is read when no account is named.</summary>
    [Fact]
    public async Task The_host_wide_schedule_is_read_when_no_account_is_named()
    {
        var accountId = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var admin = FakeCurrentUser.Admin();

        await using (var seed = BackupsTestContext.Create(admin, database))
        {
            seed.BackupSchedules.Add(Schedule(null, 3));
            seed.BackupSchedules.Add(Schedule(accountId, 11));
            await seed.SaveChangesAsync();
        }

        var handler = new GetBackupScheduleQueryHandler(BackupsTestContext.Create(admin, database));

        var hostWide = await handler.HandleAsync(new GetBackupScheduleQuery(null), CancellationToken.None);
        var override_ = await handler.HandleAsync(new GetBackupScheduleQuery(accountId), CancellationToken.None);

        Assert.NotNull(hostWide.Schedule);
        Assert.Null(hostWide.Schedule.AccountId);
        Assert.Equal(3, hostWide.Schedule.HourUtc);

        Assert.NotNull(override_.Schedule);
        Assert.Equal(accountId, override_.Schedule.AccountId);
        Assert.Equal(11, override_.Schedule.HourUtc);
    }

    /// <summary>A server with no schedule is answered with an empty scope rather than an invented default.</summary>
    /// <remarks>
    /// Not a failure (issue #211): "nothing is scheduled here" is the ordinary first state of a server.
    /// And not a default: an invented "daily at 03:00, disabled" would show an operator a schedule the
    /// server does not hold, and the first thing they would do is trust it.
    /// </remarks>
    [Fact]
    public async Task A_server_with_no_schedule_is_answered_with_an_empty_scope()
    {
        var handler = new GetBackupScheduleQueryHandler(
            BackupsTestContext.Create(FakeCurrentUser.Admin()));

        var result = await handler.HandleAsync(new GetBackupScheduleQuery(null), CancellationToken.None);

        Assert.Null(result.Schedule);
    }

    /// <summary>An account with no override is answered with an empty scope, not with the host policy.</summary>
    /// <remarks>
    /// The two scopes are different things: an account without an override is backed up by the host
    /// policy, but the screen editing that account's override must not be handed the host policy as
    /// though it were the override, or saving it would silently create one.
    /// </remarks>
    [Fact]
    public async Task An_account_with_no_override_is_answered_with_an_empty_scope_not_the_host_policy()
    {
        var database = Guid.NewGuid().ToString();
        var admin = FakeCurrentUser.Admin();

        await using (var seed = BackupsTestContext.Create(admin, database))
        {
            seed.BackupSchedules.Add(Schedule(null, 3));
            await seed.SaveChangesAsync();
        }

        var handler = new GetBackupScheduleQueryHandler(BackupsTestContext.Create(admin, database));

        var result = await handler.HandleAsync(new GetBackupScheduleQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Null(result.Schedule);
    }

    /// <summary>A customer cannot read another account's schedule, nor the host-wide one.</summary>
    /// <remarks>
    /// The surface is administrators-only, so this is the second line rather than the first: the
    /// context's tenant filter hides the rows, so the customer is told nothing is recorded — the same
    /// answer as for a scope that truly has none, which discloses nothing. That is what makes the
    /// module safe against a route added later without the policy.
    /// </remarks>
    [Fact]
    public async Task A_customer_cannot_read_a_schedule_that_is_not_theirs()
    {
        var stranger = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var seed = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seed.BackupSchedules.Add(Schedule(null, 3));
            seed.BackupSchedules.Add(Schedule(stranger, 11));
            await seed.SaveChangesAsync();
        }

        var handler = new GetBackupScheduleQueryHandler(
            BackupsTestContext.Create(FakeCurrentUser.Customer(Guid.NewGuid()), database));

        var hostWide = await handler.HandleAsync(new GetBackupScheduleQuery(null), CancellationToken.None);
        var theirs = await handler.HandleAsync(new GetBackupScheduleQuery(stranger), CancellationToken.None);

        Assert.Null(hostWide.Schedule);
        Assert.Null(theirs.Schedule);
    }

    /// <summary>Builds an enabled daily schedule.</summary>
    /// <param name="accountId">The account it names, or <c>null</c> for host-wide.</param>
    /// <param name="hourUtc">The hour it runs at.</param>
    /// <returns>The schedule.</returns>
    private static BackupSchedule Schedule(Guid? accountId, int hourUtc)
    {
        return new BackupSchedule(Guid.NewGuid(), accountId, null, BackupFrequency.Daily, hourUtc, null, 7);
    }
}
