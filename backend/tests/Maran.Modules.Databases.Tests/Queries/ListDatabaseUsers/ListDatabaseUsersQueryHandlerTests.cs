using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain.Enums;
using Maran.Modules.Databases.Queries.ListDatabaseUsers;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Databases.Tests.Queries.ListDatabaseUsers;

/// <summary>What the users list shows a caller, and what it never shows.</summary>
public sealed class ListDatabaseUsersQueryHandlerTests
{
    /// <summary>A customer is listed only the users of their own account.</summary>
    [Fact]
    public async Task A_customer_is_listed_only_the_users_of_their_own_account()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await ListAsync(world, new ListDatabaseUsersQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(["alice_reader", "alice_shopuser"], result.Value.Items.Select(row => { return row.FullName; }).Order());
        Assert.Equal(2, result.Value.Total);
        Assert.DoesNotContain(result.Value.Items, row => { return row.Id == world.StrangerUserId; });
    }

    /// <summary>A customer naming another tenants account is listed nothing.</summary>
    [Fact]
    public async Task A_customer_naming_another_tenants_account_is_listed_nothing()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await ListAsync(world, new ListDatabaseUsersQuery(AccountId: UsersTestWorld.StrangerAccountId));

        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>An administrator is listed every account's users and can narrow to one.</summary>
    [Fact]
    public async Task An_administrator_is_listed_every_accounts_users_and_can_narrow_to_one()
    {
        var world = await UsersTestWorld.CreateAsync(asAdministrator: true);

        var all = await ListAsync(world, new ListDatabaseUsersQuery());
        var narrowed = await ListAsync(world, new ListDatabaseUsersQuery(AccountId: UsersTestWorld.StrangerAccountId));

        Assert.Equal(3, all.Value.Total);
        Assert.Equal(["bob_shopuser"], narrowed.Value.Items.Select(row => { return row.FullName; }));
    }

    /// <summary>Only the dedicated user is marked dedicated and its grant names the database.</summary>
    [Fact]
    public async Task Only_the_dedicated_user_is_marked_dedicated_and_its_grant_names_the_database()
    {
        var world = await UsersTestWorld.CreateAsync();

        var result = await ListAsync(world, new ListDatabaseUsersQuery());

        var dedicated = result.Value.Items.Single(row => { return row.NameSuffix == "shopuser"; });
        var reader = result.Value.Items.Single(row => { return row.NameSuffix == "reader"; });
        Assert.True(dedicated.IsDedicated);
        Assert.False(reader.IsDedicated);

        var grant = Assert.Single(dedicated.Grants);
        Assert.Equal("alice_shop", grant.DatabaseFullName);
        Assert.Equal(
            [DatabasePrivilege.Select, DatabasePrivilege.Insert],
            grant.Privileges);
        Assert.Empty(reader.Grants);
    }

    /// <summary>The list carries no password of any kind.</summary>
    [Fact]
    public void The_list_row_carries_no_password_of_any_kind()
    {
        var names = typeof(DatabaseUserDto).GetProperties().Select(property => { return property.Name; });

        Assert.DoesNotContain(names, name => { return name.Contains("Password", StringComparison.OrdinalIgnoreCase); });
    }

    /// <summary>
    /// Sorting by name descending reverses the order. The search term is not exercised here: the in-memory
    /// provider cannot translate <c>ILIKE</c>, so the literal-match behaviour is proved against PostgreSQL in
    /// <c>DatabaseUsersAuthorizationTests</c>.
    /// </summary>
    [Fact]
    public async Task Sorting_by_name_descending_reverses_the_order()
    {
        var world = await UsersTestWorld.CreateAsync();

        var sorted = await ListAsync(
            world, new ListDatabaseUsersQuery(Sort: SortRequest.Parse("-" + DatabaseUserSortFields.Name)));

        Assert.Equal(["alice_shopuser", "alice_reader"], sorted.Value.Items.Select(row => { return row.FullName; }));
    }

    /// <summary>Runs the handler in the world's caller context.</summary>
    /// <param name="world">The world to run in.</param>
    /// <param name="query">The query to run.</param>
    private static async Task<Result<PagedResult<DatabaseUserDto>>> ListAsync(
        UsersTestWorld world,
        ListDatabaseUsersQuery query)
    {
        return await new ListDatabaseUsersQueryHandler(world.Context, world.Accounts)
            .HandleAsync(query, CancellationToken.None);
    }
}
