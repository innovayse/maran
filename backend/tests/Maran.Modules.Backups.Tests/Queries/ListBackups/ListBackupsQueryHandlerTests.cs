using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Queries.ListBackups;
using Maran.Modules.Backups.Tests.TestSupport;

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
        var result = await new ListBackupsQueryHandler(context, BackupsTestContext.FailureNames())
            .HandleAsync(new ListBackupsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([myRow.Id], result.Value.Select(backup =>
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
        var result = await new ListBackupsQueryHandler(context, BackupsTestContext.FailureNames())
            .HandleAsync(new ListBackupsQuery(), CancellationToken.None);

        Assert.Equal(3, result.Value.Count);
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
        var result = await new ListBackupsQueryHandler(context, BackupsTestContext.FailureNames())
            .HandleAsync(new ListBackupsQuery(), CancellationToken.None);

        Assert.Equal([newest.Id, oldest.Id], result.Value.Select(backup =>
        {
            return backup.Id;
        }));
    }

    /// <summary>An account with no backups is listed nothing rather than refused.</summary>
    [Fact]
    public async Task An_account_with_no_backups_is_listed_nothing()
    {
        await using var context = BackupsTestContext.Create(FakeCurrentUser.Customer(Guid.NewGuid()));
        var result = await new ListBackupsQueryHandler(context, BackupsTestContext.FailureNames())
            .HandleAsync(new ListBackupsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
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
        var result = await new ListBackupsQueryHandler(context, BackupsTestContext.FailureNames())
            .HandleAsync(new ListBackupsQuery(), CancellationToken.None);

        var listed = Assert.Single(result.Value);
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
        var result = await new ListBackupsQueryHandler(context, BackupsTestContext.FailureNames())
            .HandleAsync(new ListBackupsQuery(), CancellationToken.None);

        var listed = Assert.Single(result.Value);
        Assert.Equal(string.Empty, listed.FailureCode);
        Assert.Equal(string.Empty, listed.FailureDisplayName);
    }
}
