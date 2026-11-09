using Maran.Modules.Databases.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Databases.Tests.Persistence;

/// <summary>
/// The tenant filter on the user rows and, through them, on the grants, which carry no account id of
/// their own.
/// </summary>
public sealed class DatabaseUsersTenantFilterTests
{
    /// <summary>A customers context returns only their own users.</summary>
    [Fact]
    public async Task A_customers_context_returns_only_their_own_users()
    {
        var world = await UsersTestWorld.CreateAsync();

        var names = await world.Context.DatabaseUsers.Select(user => user.FullName).OrderBy(name => name).ToListAsync();

        Assert.Equal(["alice_reader", "alice_shopuser"], names);
    }

    /// <summary>A customers context returns only the grants of their own users.</summary>
    [Fact]
    public async Task A_customers_context_returns_only_the_grants_of_their_own_users()
    {
        var world = await UsersTestWorld.CreateAsync();

        var grants = await world.Context.DatabaseGrants.ToListAsync();

        var grant = Assert.Single(grants);
        Assert.Equal(world.DedicatedUserId, grant.DatabaseUserId);
    }

    /// <summary>An administrators context is narrowed by nothing.</summary>
    [Fact]
    public async Task An_administrators_context_is_narrowed_by_nothing()
    {
        var world = await UsersTestWorld.CreateAsync(asAdministrator: true);

        Assert.Equal(3, await world.Context.DatabaseUsers.CountAsync());
        Assert.Equal(2, await world.Context.DatabaseGrants.CountAsync());
    }

    /// <summary>A reseller sees no user and no grant because it owns no row here.</summary>
    [Fact]
    public async Task A_reseller_sees_no_user_and_no_grant_because_it_owns_no_row_here()
    {
        var world = await UsersTestWorld.CreateAsync();
        using var reseller = DatabasesTestContext.Create(FakeCurrentUser.Reseller(Guid.NewGuid()), world.Store);

        Assert.Empty(await reseller.DatabaseUsers.ToListAsync());
        Assert.Empty(await reseller.DatabaseGrants.ToListAsync());
    }

    /// <summary>A customer cannot reach another tenants user by its identifier.</summary>
    [Fact]
    public async Task A_customer_cannot_reach_another_tenants_user_by_its_identifier()
    {
        var world = await UsersTestWorld.CreateAsync();

        var found = await world.Context.DatabaseUsers.SingleOrDefaultAsync(user => user.Id == world.StrangerUserId);

        Assert.Null(found);
    }

    /// <summary>No column of the user tables is shaped like a place to keep a credential.</summary>
    [Fact]
    public void No_column_of_the_user_tables_is_shaped_like_a_place_to_keep_a_credential()
    {
        using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin());
        var columns = context.Model.GetEntityTypes()
            .Where(entity =>
            {
                return entity.GetTableName() is "DatabaseUsers" or "DatabaseGrants";
            })
            .SelectMany(entity =>
            {
                return entity.GetProperties();
            })
            .Select(property =>
            {
                return property.Name;
            })
            .ToList();

        // The positive control: the probe found the tables, so an empty answer cannot pass it blind.
        Assert.Contains("NameSuffix", columns);
        Assert.Contains("Privileges", columns);
        Assert.DoesNotContain(
            columns,
            column =>
            {
                return column.Contains("password", StringComparison.OrdinalIgnoreCase)
                    || column.Contains("secret", StringComparison.OrdinalIgnoreCase)
                    || column.Contains("credential", StringComparison.OrdinalIgnoreCase)
                    || column.Contains("hash", StringComparison.OrdinalIgnoreCase);
            });
    }
}
