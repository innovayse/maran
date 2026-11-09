using Maran.Modules.Backups.Queries.GetRestoreChoices;
using Maran.Modules.Backups.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Backups.Tests.Queries.GetRestoreChoices;

/// <summary>The databases and sites a restore of one backup can be narrowed to, and who may ask.</summary>
public sealed class GetRestoreChoicesQueryHandlerTests
{
    /// <summary>The system user name the account directory reports.</summary>
    private const string Username = "cust01";

    /// <summary>Seeds one completed backup of <paramref name="accountId"/> and returns its id.</summary>
    /// <param name="database">The in-memory database to seed.</param>
    /// <param name="accountId">The account the backup is of.</param>
    /// <returns>The backup's id.</returns>
    private static async Task<Guid> SeedAsync(string database, Guid accountId)
    {
        var row = BackupsTestContext.CompletedRow(accountId);
        await using var seeding = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        seeding.Backups.Add(row);
        await seeding.SaveChangesAsync();

        return row.Id;
    }

    /// <summary>A customer is offered their own account's databases and sites, in order.</summary>
    /// <remarks>
    /// The inverse control for the refusals below: a query mutated to answer nothing passes every
    /// "not found" case and fails here.
    /// </remarks>
    [Fact]
    public async Task A_customer_is_offered_their_accounts_databases_and_sites_in_order()
    {
        var mine = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var backupId = await SeedAsync(database, mine);
        var sites = new StubSiteDirectory();
        sites.Host(mine, "zeta.example.com");
        sites.Host(mine, "alpha.example.com");
        sites.Host(Guid.NewGuid(), "theirs.example.com");

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var result = await new GetRestoreChoicesQueryHandler(
                context,
                new StubAccountDirectory(new AccountSnapshot(mine, Username, 1, 1, 1, 1, 1, 1024, AccountState.Active)),
                new StubAccountDatabaseDirectory(mine, "cust01_shop", "cust01_blog"),
                sites)
            .HandleAsync(new GetRestoreChoicesQuery(backupId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["cust01_blog", "cust01_shop"], result.Value.Databases);
        Assert.Equal(["alpha.example.com", "zeta.example.com"], result.Value.Sites);
    }

    /// <summary>Another account's backup is not found rather than forbidden, and lists nothing of theirs.</summary>
    [Fact]
    public async Task Another_accounts_backup_is_not_found_rather_than_forbidden()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirBackup = await SeedAsync(database, theirs);

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Customer(mine), database);
        var result = await new GetRestoreChoicesQueryHandler(
                context,
                new StubAccountDirectory(),
                new StubAccountDatabaseDirectory(theirs, "cust02_shop"),
                new StubSiteDirectory())
            .HandleAsync(new GetRestoreChoicesQuery(theirBackup), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("BackupNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>A backup whose account is gone has no choices: there is nothing to restore into.</summary>
    [Fact]
    public async Task A_backup_whose_account_is_gone_offers_nothing()
    {
        var gone = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var backupId = await SeedAsync(database, gone);

        await using var context = BackupsTestContext.Create(FakeCurrentUser.Admin(), database);
        var result = await new GetRestoreChoicesQueryHandler(
                context,
                new StubAccountDirectory(),
                new StubAccountDatabaseDirectory(gone, "cust01_shop"),
                new StubSiteDirectory())
            .HandleAsync(new GetRestoreChoicesQuery(backupId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}
