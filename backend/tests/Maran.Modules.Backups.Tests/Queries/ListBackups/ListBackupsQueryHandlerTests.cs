using Maran.Modules.Backups.Common;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Queries.ListBackups;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Backups.Tests.Queries.ListBackups;

/// <summary>
/// What a listing shows: the caller's own rows, newest first, read from the panel's own table and
/// never from the agent's view of the destination.
/// </summary>
public sealed class ListBackupsQueryHandlerTests
{
    /// <summary>A customer is listed only their own backups.</summary>
    [Fact]
    public async Task A_customer_is_listed_only_their_own_backups()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var myRow = BackupsTestContext.CompletedRow(mine);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(myRow);
            seeding.Backups.Add(BackupsTestContext.CompletedRow(theirs));
            seeding.Backups.Add(BackupsTestContext.CompletedRow(theirs));
            await seeding.SaveChangesAsync();
        }

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var result = await new ListBackupsQueryHandler(
                context, BackupsTestContext.FailureNames(), new StubAccountDirectory())
            .HandleAsync(new ListBackupsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([myRow.Id], result.Value.Items.Select(backup =>
        {
            return backup.Id;
        }));
    }

    /// <summary>An administrator is listed every accounts backups.</summary>
    /// <remarks>
    /// The inverse control on the tenant axis: the same three rows, a principal no filter narrows,
    /// and all three required back. A filter mutated to hide everything fails here while the
    /// customer's test above would still pass with an empty answer.
    /// </remarks>
    [Fact]
    public async Task An_administrator_is_listed_every_accounts_backups()
    {
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(BackupsTestContext.CompletedRow(Guid.NewGuid()));
            seeding.Backups.Add(BackupsTestContext.CompletedRow(Guid.NewGuid()));
            seeding.Backups.Add(BackupsTestContext.CompletedRow(Guid.NewGuid()));
            await seeding.SaveChangesAsync();
        }

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var result = await new ListBackupsQueryHandler(
                context, BackupsTestContext.FailureNames(), new StubAccountDirectory())
            .HandleAsync(new ListBackupsQuery(), CancellationToken.None);

        Assert.Equal(3, result.Value.Total);
        Assert.Equal(3, result.Value.Items.Count);
    }

    /// <summary>The newest backup is listed first.</summary>
    [Fact]
    public async Task The_newest_backup_is_listed_first()
    {
        var accountId = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var oldest = new Backup(
            Guid.NewGuid(), accountId, null, BackupKind.Manual, BackupsTestContext.SeedInstant);
        var newest = new Backup(
            Guid.NewGuid(),
            accountId,
            null,
            BackupKind.Manual,
            BackupsTestContext.SeedInstant.AddDays(1));

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(oldest);
            seeding.Backups.Add(newest);
            await seeding.SaveChangesAsync();
        }

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Customer(accountId), database);
        var result = await new ListBackupsQueryHandler(
                context, BackupsTestContext.FailureNames(), new StubAccountDirectory())
            .HandleAsync(new ListBackupsQuery(), CancellationToken.None);

        Assert.Equal([newest.Id, oldest.Id], result.Value.Items.Select(backup =>
        {
            return backup.Id;
        }));
    }

    /// <summary>An account with no backups is listed nothing rather than refused.</summary>
    [Fact]
    public async Task An_account_with_no_backups_is_listed_nothing()
    {
        await using var context = BackupsTestContext.Create(FakeCurrentUser.Customer(Guid.NewGuid()));
        var result = await new ListBackupsQueryHandler(
                context, BackupsTestContext.FailureNames(), new StubAccountDirectory())
            .HandleAsync(new ListBackupsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>A failed row is listed with the failure NAMED, beside the code it is named from.</summary>
    /// <remarks>
    /// The pinned defect, measured in a browser: the table rendered "Не удалась AgentSystemFailure",
    /// the machine constant verbatim in the middle of localized Russian. The name is asserted as a
    /// VALUE — a test that only checked the field was non-empty would pass against the raw code,
    /// which is precisely the state that shipped. The code is asserted too, in the same test: it is
    /// what a support ticket and a log line are matched on, and moving the display name onto the
    /// wire must not have taken it away.
    /// </remarks>
    [Fact]
    public async Task A_failed_backup_is_listed_with_its_failure_named_and_its_code_kept()
    {
        var accountId = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            var row = BackupsTestContext.RunningRow(accountId);
            row.Failed("AgentSystemFailure", BackupsTestContext.SeedInstant);
            seeding.Backups.Add(row);
            await seeding.SaveChangesAsync();
        }

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var result = await new ListBackupsQueryHandler(
                context, BackupsTestContext.FailureNames(), new StubAccountDirectory())
            .HandleAsync(new ListBackupsQuery(), CancellationToken.None);

        var listed = Assert.Single(result.Value.Items);
        Assert.Equal("AgentSystemFailure", listed.FailureCode);
        Assert.Equal("The server could not complete the backup", listed.FailureDisplayName);
    }

    /// <summary>A backup that did not fail is listed with nothing in the failure name.</summary>
    /// <remarks>
    /// The other branch. A resolver that answered a sentence for the empty code would put "the
    /// server did not say why" beside every successful backup in the table.
    /// </remarks>
    [Fact]
    public async Task A_completed_backup_is_listed_with_no_failure_name_at_all()
    {
        var accountId = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(BackupsTestContext.CompletedRow(accountId));
            await seeding.SaveChangesAsync();
        }

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var result = await new ListBackupsQueryHandler(
                context, BackupsTestContext.FailureNames(), new StubAccountDirectory())
            .HandleAsync(new ListBackupsQuery(), CancellationToken.None);

        var listed = Assert.Single(result.Value.Items);
        Assert.Equal(string.Empty, listed.FailureCode);
        Assert.Equal(string.Empty, listed.FailureDisplayName);
    }


    /// <summary>An administrator naming one account is listed only that account's backups.</summary>
    /// <remarks>
    /// Run as an administrator, whom no tenant filter narrows, so the other account's row is visible
    /// to the context and only the account filter can remove it.
    /// </remarks>
    [Fact]
    public async Task An_administrator_naming_one_account_is_listed_only_that_accounts_backups()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirRow = BackupsTestContext.CompletedRow(theirs);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(BackupsTestContext.CompletedRow(mine));
            seeding.Backups.Add(theirRow);
            await seeding.SaveChangesAsync();
        }

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var result = await new ListBackupsQueryHandler(
                context, BackupsTestContext.FailureNames(), new StubAccountDirectory())
            .HandleAsync(new ListBackupsQuery(AccountId: theirs), CancellationToken.None);

        Assert.Equal([theirRow.Id], result.Value.Items.Select(backup =>
        {
            return backup.Id;
        }));
    }

    /// <summary>A customer naming another account is listed nothing.</summary>
    /// <remarks>
    /// The account filter is ANDed with the tenant filter, never in place of it: the customer's own
    /// backup exists and is not returned either, because it is not the account they named.
    /// </remarks>
    [Fact]
    public async Task A_customer_naming_another_account_is_listed_nothing()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(BackupsTestContext.CompletedRow(mine));
            seeding.Backups.Add(BackupsTestContext.CompletedRow(theirs));
            await seeding.SaveChangesAsync();
        }

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var result = await new ListBackupsQueryHandler(
                context, BackupsTestContext.FailureNames(), new StubAccountDirectory())
            .HandleAsync(new ListBackupsQuery(AccountId: theirs), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>
    /// A listing labels each row with its owner's current name from the account directory, in one
    /// batched lookup; a row whose account the directory does not name — here a deleted one — gets an
    /// empty owner name and keeps saying whose it was through its orphaned username alone.
    /// </summary>
    [Fact]
    public async Task Rows_carry_the_owner_name_the_directory_answers_and_empty_for_one_it_does_not()
    {
        var live = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var liveRow = BackupsTestContext.CompletedRow(live);
        var orphanRow = BackupsTestContext.CompletedRow(deleted);
        orphanRow.Orphan("gone01");

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(liveRow);
            seeding.Backups.Add(orphanRow);
            await seeding.SaveChangesAsync();
        }

        // The directory names the live account only, which is how the real one answers once an
        // account has been deleted.
        var accounts = new StubAccountDirectory(
            new AccountSnapshot(live, "cust01", 1, 1, 1, 1, 1, 1024, AccountState.Active));

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var result = await new ListBackupsQueryHandler(context, BackupsTestContext.FailureNames(), accounts)
            .HandleAsync(new ListBackupsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var liveBackup = result.Value.Items.Single(backup =>
        {
            return backup.Id == liveRow.Id;
        });
        Assert.Equal("cust01", liveBackup.OwnerName);
        Assert.Equal(string.Empty, liveBackup.OrphanedAccountUsername);

        var orphanBackup = result.Value.Items.Single(backup =>
        {
            return backup.Id == orphanRow.Id;
        });
        Assert.Equal(string.Empty, orphanBackup.OwnerName);
        Assert.Equal("gone01", orphanBackup.OrphanedAccountUsername);

        var batch = Assert.Single(accounts.NameLookups);
        Assert.Equal(new[] { live, deleted }.Order(), batch.Order());
    }

    /// <summary>The status, kind and start-window filters each narrow the page and its total.</summary>
    [Fact]
    public async Task The_status_kind_and_date_filters_each_narrow_the_page_and_its_total()
    {
        var accountId = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var early = BackupsTestContext.CompletedRow(accountId);
        var failed = new Backup(
            Guid.NewGuid(), accountId, null, BackupKind.Manual, BackupsTestContext.SeedInstant.AddDays(2));
        failed.Failed("AgentSystemFailure", BackupsTestContext.SeedInstant.AddDays(2));
        var scheduled = new Backup(
            Guid.NewGuid(), accountId, null, BackupKind.Scheduled, BackupsTestContext.SeedInstant.AddDays(5));

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.AddRange(early, failed, scheduled);
            await seeding.SaveChangesAsync();
        }

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var handler = new ListBackupsQueryHandler(
            context, BackupsTestContext.FailureNames(), new StubAccountDirectory());

        var byStatus = await handler.HandleAsync(
            new ListBackupsQuery(Status: BackupStatus.Failed), CancellationToken.None);
        Assert.Equal([failed.Id], Ids(byStatus.Value));
        Assert.Equal(1, byStatus.Value.Total);

        var byKind = await handler.HandleAsync(
            new ListBackupsQuery(Kind: BackupKind.Scheduled), CancellationToken.None);
        Assert.Equal([scheduled.Id], Ids(byKind.Value));

        var byWindow = await handler.HandleAsync(
            new ListBackupsQuery(
                From: BackupsTestContext.SeedInstant.AddDays(1),
                To: BackupsTestContext.SeedInstant.AddDays(2)),
            CancellationToken.None);
        Assert.Equal([failed.Id], Ids(byWindow.Value));
    }

    /// <summary>Both ends of the start window are inclusive.</summary>
    [Fact]
    public async Task A_backup_started_exactly_on_either_bound_is_inside_the_window()
    {
        var accountId = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var row = BackupsTestContext.CompletedRow(accountId);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(row);
            await seeding.SaveChangesAsync();
        }

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var result = await new ListBackupsQueryHandler(
                context, BackupsTestContext.FailureNames(), new StubAccountDirectory())
            .HandleAsync(
                new ListBackupsQuery(From: row.StartedAt, To: row.StartedAt),
                CancellationToken.None);

        Assert.Equal([row.Id], Ids(result.Value));
    }

    /// <summary>A size sort orders by the artifact's size, and a page past the end answers empty with the total.</summary>
    [Fact]
    public async Task A_size_sort_orders_by_size_and_a_page_past_the_end_is_empty_with_the_true_total()
    {
        var accountId = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var small = new Backup(Guid.NewGuid(), accountId, null, BackupKind.Manual, BackupsTestContext.SeedInstant);
        small.Completed(10, "aa", 0, BackupsTestContext.SeedInstant);
        var large = new Backup(Guid.NewGuid(), accountId, null, BackupKind.Manual, BackupsTestContext.SeedInstant);
        large.Completed(1000, "bb", 0, BackupsTestContext.SeedInstant);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.AddRange(small, large);
            await seeding.SaveChangesAsync();
        }

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var handler = new ListBackupsQueryHandler(
            context, BackupsTestContext.FailureNames(), new StubAccountDirectory());

        var bySize = await handler.HandleAsync(
            new ListBackupsQuery(Sort: SortRequest.Parse("-" + BackupSortFields.Size)), CancellationToken.None);
        Assert.Equal([large.Id, small.Id], Ids(bySize.Value));

        var pastTheEnd = await handler.HandleAsync(new ListBackupsQuery(Page: 9), CancellationToken.None);
        Assert.Empty(pastTheEnd.Value.Items);
        Assert.Equal(2, pastTheEnd.Value.Total);
        Assert.Equal(9, pastTheEnd.Value.Page);
    }

    /// <summary>The ids of a page's rows, in order.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The ids.</returns>
    private static List<Guid> Ids(PagedResult<BackupDto> page)
    {
        return page.Items
            .Select(backup =>
            {
                return backup.Id;
            })
            .ToList();
    }
}
