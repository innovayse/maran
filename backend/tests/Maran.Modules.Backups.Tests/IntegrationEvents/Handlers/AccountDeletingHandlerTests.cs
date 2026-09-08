using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.IntegrationEvents.Handlers;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Events;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Backups.Tests.IntegrationEvents.Handlers;

/// <summary>
/// What an account's deletion does to this module's rows: every backup of that account goes except
/// the final one taken for that very deletion, and nothing else goes at all.
/// </summary>
/// <remarks>
/// The residue this handler exists to prevent is what the runtime auditor refuses a deletion over,
/// so "the rows are gone" is the whole contract — asserted with the query filter bypassed, because a
/// read that the filter narrowed would report an empty table for a row that is still there.
/// </remarks>
public sealed class AccountDeletingHandlerTests
{
    /// <summary>Every backup of the deleted account is removed.</summary>
    [Fact]
    public async Task Every_backup_of_the_deleted_account_is_removed()
    {
        var doomed = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(BackupsTestContext.CompletedRow(doomed));
            seeding.Backups.Add(BackupsTestContext.RunningRow(doomed));
            await seeding.SaveChangesAsync();
        }

        // The vacuity guard, on the axis that goes blind: a handler that deleted nothing and a seed
        // that wrote nothing produce the same empty table afterwards.
        await using (var auditing = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            Assert.Equal(2, await auditing.Backups.IgnoreQueryFilters().CountAsync());
        }

        await using (var handling = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            await new AccountDeletingHandler(handling)
                .HandleAsync(new AccountDeleting(doomed, "doomed"), CancellationToken.None);
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Empty(await reading.Backups.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>Another accounts backups survive the deletion.</summary>
    /// <remarks>
    /// The inverse control. A handler mutated to remove every row — dropping the <c>Where</c>
    /// clause, which is the one-line mutation this cascade is most exposed to — satisfies the test
    /// above completely and destroys every other customer's backups. Only this test sees it.
    /// </remarks>
    [Fact]
    public async Task Another_accounts_backups_survive_the_deletion()
    {
        var doomed = Guid.NewGuid();
        var bystander = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var survivor = BackupsTestContext.CompletedRow(bystander);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(BackupsTestContext.CompletedRow(doomed));
            seeding.Backups.Add(survivor);
            await seeding.SaveChangesAsync();
        }

        await using (var handling = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            await new AccountDeletingHandler(handling)
                .HandleAsync(new AccountDeleting(doomed, "doomed"), CancellationToken.None);
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var left = await reading.Backups.IgnoreQueryFilters().ToListAsync();

        Assert.Equal([survivor.Id], left.Select(backup =>
        {
            return backup.Id;
        }));
    }

    /// <summary>The rows are removed whichever tenant the deletion is running as.</summary>
    /// <remarks>
    /// The deliberate filter bypass, observed rather than asserted about. The context here belongs
    /// to a DIFFERENT customer, which is the situation an unattended cascade runs in: with the
    /// filter honoured the handler would find no rows and report a clean deletion over an account
    /// whose backups are all still listed. Removing the <c>IgnoreQueryFilters</c> call fails this
    /// test and nothing else here.
    /// </remarks>
    [Fact]
    public async Task The_rows_are_removed_whichever_tenant_the_deletion_runs_as()
    {
        var doomed = Guid.NewGuid();
        var somebodyElse = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(BackupsTestContext.CompletedRow(doomed));
            await seeding.SaveChangesAsync();
        }

        await using (var handling = BackupsTestContext.Create(FakeCurrentUser.Customer(somebodyElse), database))
        {
            await new AccountDeletingHandler(handling)
                .HandleAsync(new AccountDeleting(doomed, "doomed"), CancellationToken.None);
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Empty(await reading.Backups.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>An account with no backups is deleted without complaint.</summary>
    [Fact]
    public async Task An_account_with_no_backups_is_deleted_without_complaint()
    {
        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin());

        await new AccountDeletingHandler(context)
            .HandleAsync(new AccountDeleting(Guid.NewGuid(), "empty"), CancellationToken.None);

        Assert.Empty(await context.Backups.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>The final backup taken for this deletion survives it.</summary>
    /// <remarks>
    /// The §12 promise, and the reason the cascade has an exemption at all: a copy taken immediately
    /// before an account is destroyed is the last copy of that customer's data, and a cascade that
    /// swept it away would be deleting the safety net in the same breath as the thing it was a net
    /// for. Its <c>AccountId</c> then names an account that is gone, which is intended.
    /// </remarks>
    [Fact]
    public async Task The_pre_deletion_backup_survives_the_deletion_it_was_taken_for()
    {
        var doomed = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var final = BackupsTestContext.CompletedRow(doomed, kind: BackupKind.PreDeletion);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(BackupsTestContext.CompletedRow(doomed));
            seeding.Backups.Add(final);
            await seeding.SaveChangesAsync();
        }

        await using (var handling = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            await new AccountDeletingHandler(handling)
                .HandleAsync(new AccountDeleting(doomed, "doomed"), CancellationToken.None);
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var left = await reading.Backups.IgnoreQueryFilters().ToListAsync();

        Assert.Equal([final.Id], left.Select(backup =>
        {
            return backup.Id;
        }));
    }

    /// <summary>A backup of any OTHER kind is still removed by the cascade.</summary>
    /// <remarks>
    /// The narrowing, tested on its own axis. The exemption is the one hole in the check that caught
    /// the cascade defect, so what has to be pinned is not that it works but that it does not
    /// WIDEN: a mutation that exempted every kind, or every completed backup, would satisfy the test
    /// above and would leave a customer's entire archive listed against an account that is gone.
    /// </remarks>
    [Fact]
    public async Task A_backup_of_any_other_kind_is_still_removed()
    {
        var doomed = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(BackupsTestContext.CompletedRow(doomed, kind: BackupKind.Manual));
            seeding.Backups.Add(BackupsTestContext.CompletedRow(doomed, kind: BackupKind.Scheduled));
            seeding.Backups.Add(BackupsTestContext.CompletedRow(doomed, kind: BackupKind.PreRestore));
            await seeding.SaveChangesAsync();
        }

        await using (var handling = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            await new AccountDeletingHandler(handling)
                .HandleAsync(new AccountDeleting(doomed, "doomed"), CancellationToken.None);
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Empty(await reading.Backups.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>The kept backup is stamped with the deleted account's user name.</summary>
    /// <remarks>
    /// <para>
    /// The stamp is what makes the row releasable afterwards. An archive is addressed by the
    /// account's system user name, and this is the last moment that name exists anywhere — the
    /// accounts row is gone a moment later, so a row left unstamped names an archive nothing can
    /// ever find or delete, which is the state every pre-deletion backup was in.
    /// </para>
    /// <para>
    /// The removed rows are NOT stamped, which is the inverse control: a stamp applied to everything
    /// would pass an assertion that only looked at the survivor.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_kept_backup_is_stamped_with_the_deleted_accounts_user_name()
    {
        var doomed = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(BackupsTestContext.CompletedRow(doomed, kind: BackupKind.PreDeletion));
            seeding.Backups.Add(BackupsTestContext.CompletedRow(doomed));
            await seeding.SaveChangesAsync();
        }

        await using (var handling = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            await new AccountDeletingHandler(handling)
                .HandleAsync(new AccountDeleting(doomed, "doomed"), CancellationToken.None);
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var kept = Assert.Single(await reading.Backups.IgnoreQueryFilters().ToListAsync());

        Assert.Equal(BackupKind.PreDeletion, kept.Kind);
        Assert.True(kept.NamesADeletedAccount());
        Assert.Equal("doomed", kept.OrphanedAccountUsername);
    }

    /// <summary>The deleted account's own backup schedule goes with it.</summary>
    /// <remarks>
    /// A schedule for an account that no longer exists is an instruction to back up nothing, and the
    /// runtime residue auditor refuses the deletion over any row that still names the account — so
    /// without this the account would not be deletable at all.
    /// </remarks>
    [Fact]
    public async Task The_deleted_accounts_own_schedule_goes_with_it()
    {
        var doomed = Guid.NewGuid();
        var survivor = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.BackupSchedules.Add(Schedule(doomed));
            seeding.BackupSchedules.Add(Schedule(survivor));
            seeding.BackupSchedules.Add(Schedule(null));
            await seeding.SaveChangesAsync();
        }

        await using (var handling = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            await new AccountDeletingHandler(handling)
                .HandleAsync(new AccountDeleting(doomed, "doomed"), CancellationToken.None);
        }

        await using var reading = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var remaining = await reading.BackupSchedules.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, row => { return row.AccountId == doomed; });
        Assert.Contains(remaining, row => { return row.AccountId == survivor; });
        Assert.Contains(remaining, row => { return row.AccountId is null; });
    }

    /// <summary>A null event is refused rather than read as an empty account.</summary>
    [Fact]
    public async Task A_null_event_is_refused()
    {
        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin());
        var handler = new AccountDeletingHandler(context);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await handler.HandleAsync(null!, CancellationToken.None);
        });
    }

    /// <summary>Builds a disabled daily schedule.</summary>
    /// <param name="accountId">The account it names, or <c>null</c> for the host-wide policy.</param>
    /// <returns>The schedule.</returns>
    private static BackupSchedule Schedule(Guid? accountId)
    {
        return new BackupSchedule(Guid.NewGuid(), accountId, null, BackupFrequency.Daily, 3, null, 7);
    }
}
