using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.ValueObjects;
using Maran.Modules.Backups.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.Persistence;

/// <summary>
/// The Backups module's tenant query filter, tested as the thing it is: ONE database holding two
/// customers' rows, and two contexts that must not see each other's (spec §8). Seeding each tenant
/// into its own database would let the setup do the separating, and the filter could then be
/// deleted with every test here still green.
/// </summary>
public sealed class BackupsDbContextTenantFilterTests
{
    /// <summary>A customer sees only their own backups.</summary>
    [Fact]
    public async Task A_customer_sees_only_their_own_backups()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var myBackup = BackupsTestContext.CompletedRow(mine);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(myBackup);
            seeding.Backups.Add(BackupsTestContext.CompletedRow(theirs));
            await seeding.SaveChangesAsync();
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var visible = await reading.Backups.ToListAsync();

        Assert.Equal([myBackup.Id], visible.Select(backup =>
        {
            return backup.Id;
        }));
    }

    /// <summary>Another customers backup is not found rather than forbidden.</summary>
    /// <remarks>
    /// The refusing half. Its inverse control is
    /// <see cref="The_owning_customer_does_find_the_same_row"/>, which hands the SAME row to the
    /// account that owns it and requires it back: a filter mutated to hide everything would satisfy
    /// this test alone.
    /// </remarks>
    [Fact]
    public async Task Another_customers_backup_is_not_found_rather_than_forbidden()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirBackup = BackupsTestContext.CompletedRow(theirs);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(theirBackup);
            await seeding.SaveChangesAsync();
        }

        // The vacuity guard, on the axis that goes blind: a seeding step that wrote nothing would
        // make "the row is not visible" true for the wrong reason, and every assertion below it
        // would be decoration.
        await using (var auditing = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            Assert.Equal(1, await auditing.Backups.CountAsync());
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var found = await reading.Backups.FirstOrDefaultAsync(backup => backup.Id == theirBackup.Id);

        // Not found, so the handler above answers 404. A 403 would confirm the row exists, which is
        // the whole of what an enumeration of backup identifiers wants (rules/security.md item 6).
        Assert.Null(found);
    }

    /// <summary>The owning customer does find the same row.</summary>
    /// <remarks>The inverse control for the refusal above: the gate must ACCEPT the valid case.</remarks>
    [Fact]
    public async Task The_owning_customer_does_find_the_same_row()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var myBackup = BackupsTestContext.CompletedRow(mine);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(myBackup);
            await seeding.SaveChangesAsync();
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var found = await reading.Backups.FirstOrDefaultAsync(backup => backup.Id == myBackup.Id);

        Assert.NotNull(found);
    }

    /// <summary>An administrator sees every accounts backups.</summary>
    [Fact]
    public async Task An_administrator_sees_every_accounts_backups()
    {
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(BackupsTestContext.CompletedRow(Guid.NewGuid()));
            seeding.Backups.Add(BackupsTestContext.CompletedRow(Guid.NewGuid()));
            await seeding.SaveChangesAsync();
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);

        Assert.Equal(2, await reading.Backups.CountAsync());
    }

    /// <summary>A caller with no account does not see the host-wide schedule (issue #438).</summary>
    /// <remarks>
    /// The refusing half, and the one the defect lived in: the host-wide row has a null
    /// <c>AccountId</c>, and a filter comparing it to a null tenant matches it. Its inverse controls
    /// are <see cref="An_administrator_sees_the_host_wide_schedule_and_every_accounts_own"/> and
    /// <see cref="A_customer_sees_only_their_own_schedule"/>: a filter mutated to hide everything
    /// would satisfy this test alone, and the seeding audit below proves the row is there to be hidden.
    /// </remarks>
    [Fact]
    public async Task A_caller_with_no_account_does_not_see_the_host_wide_schedule()
    {
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.BackupSchedules.Add(NewSchedule(null));
            seeding.BackupSchedules.Add(NewSchedule(Guid.NewGuid()));
            await seeding.SaveChangesAsync();
        }

        await using (var auditing = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            Assert.Equal(1, await auditing.BackupSchedules.CountAsync(row => row.AccountId == null));
        }

        var accountless = new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: false);

        await using var accountlessReading = BackupsTestContext.Create(accountless, database);
        Assert.Empty(await accountlessReading.BackupSchedules.ToListAsync());

        await using var resellerReading = BackupsTestContext.Create(
            FakeCurrentUser.Reseller(Guid.NewGuid()),
            database);
        Assert.Empty(await resellerReading.BackupSchedules.ToListAsync());
    }

    /// <summary>A customer sees only their own schedule, never the host-wide one nor another account's.</summary>
    [Fact]
    public async Task A_customer_sees_only_their_own_schedule()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var mySchedule = NewSchedule(mine);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.BackupSchedules.Add(NewSchedule(null));
            seeding.BackupSchedules.Add(mySchedule);
            seeding.BackupSchedules.Add(NewSchedule(Guid.NewGuid()));
            await seeding.SaveChangesAsync();
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);

        var visible = await reading.BackupSchedules.ToListAsync();

        Assert.Equal([mySchedule.Id], visible.Select(row =>
        {
            return row.Id;
        }));
    }

    /// <summary>An administrator sees the host-wide schedule and every account's own.</summary>
    [Fact]
    public async Task An_administrator_sees_the_host_wide_schedule_and_every_accounts_own()
    {
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.BackupSchedules.Add(NewSchedule(null));
            seeding.BackupSchedules.Add(NewSchedule(Guid.NewGuid()));
            await seeding.SaveChangesAsync();
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);

        Assert.Equal(2, await reading.BackupSchedules.CountAsync());
        Assert.Equal(1, await reading.BackupSchedules.CountAsync(row => row.AccountId == null));
    }

    /// <summary>Builds a daily schedule for an account, or the host-wide one when none is named.</summary>
    /// <param name="accountId">The account, or <c>null</c> for the host-wide policy.</param>
    /// <returns>A new schedule row.</returns>
    private static BackupSchedule NewSchedule(Guid? accountId)
    {
        return new BackupSchedule(Guid.NewGuid(), accountId, null, new DailyBackupCadence(3), 7);
    }

    /// <summary>A customer sees only their own account's opt-out row, never another's.</summary>
    /// <remarks>
    /// The IDOR test for the entity: the other account's row is hidden (not found, so any handler would
    /// answer 404), and the inverse control hands the same row to its owner.
    /// </remarks>
    [Fact]
    public async Task A_customer_sees_only_their_own_opt_out_row()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.AccountBackupOptOuts.Add(new AccountBackupOptOut(mine, BackupsTestContext.SeedInstant));
            seeding.AccountBackupOptOuts.Add(new AccountBackupOptOut(theirs, BackupsTestContext.SeedInstant));
            await seeding.SaveChangesAsync();
        }

        // The vacuity guard: both rows exist for an administrator, so the narrowing below is the filter.
        await using (var auditing = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            Assert.Equal(2, await auditing.AccountBackupOptOuts.CountAsync());
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var visible = await reading.AccountBackupOptOuts.ToListAsync();

        Assert.Equal([mine], visible.Select(row => { return row.AccountId; }));
        Assert.Null(await reading.AccountBackupOptOuts.FirstOrDefaultAsync(row => row.AccountId == theirs));
    }

    /// <summary>A caller with no account, and a reseller, see no opt-out row at all.</summary>
    [Fact]
    public async Task A_caller_with_no_account_sees_no_opt_out_row()
    {
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.AccountBackupOptOuts.Add(new AccountBackupOptOut(Guid.NewGuid(), BackupsTestContext.SeedInstant));
            await seeding.SaveChangesAsync();
        }

        var accountless = new FakeCurrentUser(Guid.NewGuid(), null, isAdmin: false);

        await using var accountlessReading = BackupsTestContext.Create(accountless, database);
        Assert.Empty(await accountlessReading.AccountBackupOptOuts.ToListAsync());

        await using var resellerReading = BackupsTestContext.Create(FakeCurrentUser.Reseller(Guid.NewGuid()), database);
        Assert.Empty(await resellerReading.AccountBackupOptOuts.ToListAsync());
    }

    /// <summary>An administrator sees every account's opt-out row.</summary>
    [Fact]
    public async Task An_administrator_sees_every_opt_out_row()
    {
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.AccountBackupOptOuts.Add(new AccountBackupOptOut(Guid.NewGuid(), BackupsTestContext.SeedInstant));
            seeding.AccountBackupOptOuts.Add(new AccountBackupOptOut(Guid.NewGuid(), BackupsTestContext.SeedInstant));
            await seeding.SaveChangesAsync();
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);

        Assert.Equal(2, await reading.AccountBackupOptOuts.CountAsync());
    }
}
