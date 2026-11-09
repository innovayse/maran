using Maran.Agent.Client.Services.BackupService;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Jobs;
using Maran.Modules.Backups.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Jobs;

/// <summary>
/// The nightly sweep and the accounts created without backups: an opted-out account is not backed up by
/// any schedule, the accounts around it still are, and the sweep finds the opt-out whichever principal
/// it runs as.
/// </summary>
public sealed class BackupRunHandlerOptOutTests
{
    /// <summary>The first five-minute tick past the schedule's named hour.</summary>
    private static readonly DateTimeOffset FirstTick = BackupSweepWorld.NamedHour.AddMinutes(1);

    /// <summary>A terminal event describing a finished archive.</summary>
    private static readonly BackupCreateEvent Created =
        new(BackupCreateEventKind.Created, 100, "done", 4096, new string('b', 64), 3, null);

    /// <summary>Records that an account is left out of scheduled backups, in the world's own database.</summary>
    /// <param name="world">The world the sweep runs in.</param>
    /// <param name="accountId">The account to leave out.</param>
    private static async Task OptOutAsync(BackupSweepWorld world, Guid accountId)
    {
        await using var seed = world.Read();
        seed.AccountBackupOptOuts.Add(new AccountBackupOptOut(accountId, BackupsTestContext.SeedInstant));
        await seed.SaveChangesAsync();
    }

    /// <summary>The host-wide schedule skips an opted-out account and still backs up the other.</summary>
    [Fact]
    public async Task The_host_wide_schedule_skips_an_opted_out_account_and_backs_up_the_other()
    {
        var left = Guid.NewGuid();
        var kept = Guid.NewGuid();
        var world = await BackupSweepWorld.WithScheduleAsync(
            kept, null, FirstTick, [Created], secondAccountId: left);
        await OptOutAsync(world, left);

        var started = await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Equal(1, started);
        var row = Assert.Single(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(kept, row.AccountId);
    }

    /// <summary>The same sweep backs up both accounts when nobody is opted out.</summary>
    /// <remarks>The inverse control: a sweep that backed up nothing would pass the test above.</remarks>
    [Fact]
    public async Task The_same_sweep_backs_up_both_accounts_when_nobody_is_opted_out()
    {
        var world = await BackupSweepWorld.WithScheduleAsync(
            Guid.NewGuid(), null, FirstTick, [Created], secondAccountId: Guid.NewGuid());

        var started = await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Equal(2, started);
    }

    /// <summary>A schedule that names the opted-out account does not take its backup either.</summary>
    [Fact]
    public async Task A_schedule_naming_the_opted_out_account_does_not_back_it_up()
    {
        var left = Guid.NewGuid();
        var world = await BackupSweepWorld.WithScheduleAsync(left, left, FirstTick, [Created]);
        await OptOutAsync(world, left);

        var started = await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Equal(0, started);
        Assert.Empty(world.Agent.Creates);
        Assert.Empty(await world.Read().Backups.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>The sweep finds the opt-out whichever principal it runs as.</summary>
    /// <remarks>
    /// The deliberate filter bypass, observed rather than asserted about: the sweep has no principal, and
    /// the tenant filter shows a caller with no account nothing. With the bypass removed the opt-out row
    /// would be invisible and the account backed up every night; this test then reports a backup.
    /// </remarks>
    [Fact]
    public async Task The_sweep_finds_the_opt_out_whichever_principal_it_runs_as()
    {
        var left = Guid.NewGuid();
        var accountless = new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: false);
        var world = await BackupSweepWorld.WithScheduleAsync(left, null, FirstTick, [Created], caller: accountless);
        await OptOutAsync(world, left);

        // The vacuity guard: the principal the sweep runs as cannot see the row it is looking for.
        await using (var asTheSweep = world.Read())
        {
            Assert.Empty(await asTheSweep.AccountBackupOptOuts.ToListAsync());
            Assert.Single(await asTheSweep.AccountBackupOptOuts.IgnoreQueryFilters().ToListAsync());
        }

        var started = await world.Handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Equal(0, started);
        Assert.Empty(world.Agent.Creates);
    }
}
