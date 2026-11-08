using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Domain.Policies;
using Maran.Modules.Databases.Seeders;
using Maran.Modules.Databases.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Databases.Tests.Seeders;

/// <summary>That every database ends up with its dedicated user row, once, whoever runs the pass.</summary>
public sealed class DatabaseUserBackfillSeederTests
{
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>A database with no user row gets one holding every privilege on it.</summary>
    [Fact]
    public async Task A_database_with_no_user_row_gets_one_holding_every_privilege_on_it()
    {
        var store = Guid.NewGuid().ToString();
        var database = await SeedDatabaseAsync(store, OwnerAccountId, "alice", "shop", "shopuser");

        var created = await SeedAsync(store);

        Assert.Equal(1, created);
        var user = Assert.Single(await ReadUsersAsync(store));
        Assert.Equal("alice_shopuser", user.FullName);
        Assert.Equal("shopuser", user.NameSuffix);
        var grant = Assert.Single(user.Grants);
        Assert.Equal(database.Id, grant.DatabaseId);
        Assert.Equal(DatabasePrivilegeSets.Full, grant.Privileges);
    }

    /// <summary>The pass reaches every account's databases though it runs as nobody.</summary>
    [Fact]
    public async Task The_pass_reaches_every_accounts_databases_though_it_runs_as_nobody()
    {
        var store = Guid.NewGuid().ToString();
        await SeedDatabaseAsync(store, OwnerAccountId, "alice", "shop", "shopuser");
        await SeedDatabaseAsync(store, StrangerAccountId, "bob", "shop", "shopuser");

        var created = await SeedAsync(store);

        Assert.Equal(2, created);
        Assert.Equal(
            ["alice_shopuser", "bob_shopuser"],
            (await ReadUsersAsync(store)).Select(user => { return user.FullName; }).Order());
    }

    /// <summary>A second pass creates nothing.</summary>
    [Fact]
    public async Task A_second_pass_creates_nothing()
    {
        var store = Guid.NewGuid().ToString();
        await SeedDatabaseAsync(store, OwnerAccountId, "alice", "shop", "shopuser");
        await SeedAsync(store);

        var second = await SeedAsync(store);

        Assert.Equal(0, second);
        Assert.Single(await ReadUsersAsync(store));
    }

    /// <summary>A user the customer already shaped is left exactly as it is.</summary>
    [Fact]
    public async Task A_user_the_customer_already_shaped_is_left_exactly_as_it_is()
    {
        var store = Guid.NewGuid().ToString();
        var database = await SeedDatabaseAsync(store, OwnerAccountId, "alice", "shop", "shopuser");
        using (var admin = DatabasesTestContext.Create(FakeCurrentUser.Admin(), store))
        {
            var user = new DatabaseUser(Guid.NewGuid(), OwnerAccountId, "shopuser", "alice_shopuser", UsersTestWorld.Now);
            user.ReplacePrivileges(database.Id, DatabasePrivilegeSet.Select, Guid.NewGuid(), UsersTestWorld.Now);
            admin.DatabaseUsers.Add(user);
            await admin.SaveChangesAsync();
        }

        var created = await SeedAsync(store);

        Assert.Equal(0, created);
        Assert.Equal(DatabasePrivilegeSet.Select, Assert.Single(Assert.Single(await ReadUsersAsync(store)).Grants).Privileges);
    }

    /// <summary>Runs the seeder with no principal, as the Host's start-up task does.</summary>
    /// <param name="store">The store to seed.</param>
    private static async Task<int> SeedAsync(string store)
    {
        using var context = DatabasesTestContext.Create(FakeCurrentUser.Customer(Guid.NewGuid()), store);

        return await new DatabaseUserBackfillSeeder(context, new FakeClock(UsersTestWorld.Now)).SeedAsync(CancellationToken.None);
    }

    /// <summary>Writes one database row the way the previous release did, with no user row.</summary>
    /// <param name="store">The store to write to.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="username">The account's system user name.</param>
    /// <param name="name">The database name suffix.</param>
    /// <param name="dbUserName">The dedicated user's suffix.</param>
    private static async Task<Database> SeedDatabaseAsync(
        string store,
        Guid accountId,
        string username,
        string name,
        string dbUserName)
    {
        using var admin = DatabasesTestContext.Create(FakeCurrentUser.Admin(), store);
        var row = DatabasesTestContext.Row(accountId, username, name, dbUserName);
        admin.Databases.Add(row);
        await admin.SaveChangesAsync();

        return row;
    }

    /// <summary>Reads every user row of the store with its grants.</summary>
    /// <param name="store">The store to read.</param>
    private static async Task<List<DatabaseUser>> ReadUsersAsync(string store)
    {
        using var admin = DatabasesTestContext.Create(FakeCurrentUser.Admin(), store);

        return await admin.DatabaseUsers.AsNoTracking().Include(user => user.Grants).ToListAsync();
    }
}
