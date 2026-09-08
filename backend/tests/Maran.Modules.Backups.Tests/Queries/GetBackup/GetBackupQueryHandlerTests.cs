using Maran.Modules.Backups.Queries.GetBackup;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Backups.Tests.Queries.GetBackup;

/// <summary>Reading one backup, and what another account's identifier answers.</summary>
public sealed class GetBackupQueryHandlerTests
{
    /// <summary>A customer reads their own backup.</summary>
    /// <remarks>
    /// The inverse control for the refusal below: a query filter mutated to hide every row would
    /// pass "another account's backup is not found" and fail here.
    /// </remarks>
    [Fact]
    public async Task A_customer_reads_their_own_backup()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var row = BackupsTestContext.CompletedRow(mine);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(row);
            await seeding.SaveChangesAsync();
        }

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var result = await new GetBackupQueryHandler(context, BackupsTestContext.FailureNames())
            .HandleAsync(new GetBackupQuery(row.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(row.Id, result.Value.Id);
        Assert.Equal(mine, result.Value.AccountId);
    }

    /// <summary>Another accounts backup is not found rather than forbidden.</summary>
    [Fact]
    public async Task Another_accounts_backup_is_not_found_rather_than_forbidden()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirRow = BackupsTestContext.CompletedRow(theirs);

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(theirRow);
            await seeding.SaveChangesAsync();
        }

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var result = await new GetBackupQueryHandler(context, BackupsTestContext.FailureNames())
            .HandleAsync(new GetBackupQuery(theirRow.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BackupNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.NotEqual(ErrorType.Forbidden, result.Error.Type);
    }

    /// <summary>An administrator reads any accounts backup.</summary>
    [Fact]
    public async Task An_administrator_reads_any_accounts_backup()
    {
        var database = Guid.NewGuid().ToString();
        var row = BackupsTestContext.CompletedRow(Guid.NewGuid());

        await using (var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database))
        {
            seeding.Backups.Add(row);
            await seeding.SaveChangesAsync();
        }

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var result = await new GetBackupQueryHandler(context, BackupsTestContext.FailureNames())
            .HandleAsync(new GetBackupQuery(row.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A backup identifier nobody owns is not found.</summary>
    [Fact]
    public async Task A_backup_identifier_nobody_owns_is_not_found()
    {
        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin());
        var result = await new GetBackupQueryHandler(context, BackupsTestContext.FailureNames())
            .HandleAsync(new GetBackupQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }
}
