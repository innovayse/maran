using Maran.Modules.Databases.Domain;
using Maran.Modules.Databases.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Databases.Tests.Persistence;

/// <summary>
/// The module's authorisation mechanism, tested on its own rather than only through the handlers
/// that lean on it.
/// </summary>
public sealed class DatabasesDbContextTenantFilterTests
{
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>
    /// The words a column that holds a credential would plausibly be named with. Deliberately wider
    /// than "password": the failure to catch is somebody storing the value under a name that sounds
    /// safe, and "not plaintext, it is a hash" is exactly the reasoning that would produce one.
    /// </summary>
    private static readonly string[] SecretShapedWords = ["password", "secret", "credential", "hash"];

    /// <summary>A customers context returns only their own rows.</summary>
    [Fact]
    public async Task A_customers_context_returns_only_their_own_rows()
    {
        var store = await SeedAsync();

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), store);

        Assert.Equal(["shop"], await context.Databases.Select(row => row.Name).ToListAsync());
    }

    /// <summary>An administrators context is narrowed by nothing.</summary>
    [Fact]
    public async Task An_administrators_context_is_narrowed_by_nothing()
    {
        var store = await SeedAsync();

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin(), store);

        Assert.Equal(2, await context.Databases.CountAsync());
    }

    /// <summary>A customer cannot reach another tenants row by its identifier.</summary>
    [Fact]
    public async Task A_customer_cannot_reach_another_tenants_row_by_its_identifier()
    {
        // This is why the module answers 404 and not 403: the row is not in the result set at all,
        // so nothing in a handler has to remember to make the distinction.
        var store = await SeedAsync();

        using var admin = DatabasesTestContext.Create(FakeCurrentUser.Admin(), store);
        var stranger = await admin.Databases.SingleAsync(row => row.AccountId == StrangerAccountId);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), store);

        Assert.Null(await context.Databases.SingleOrDefaultAsync(row => row.Id == stranger.Id));
    }

    /// <summary>The database entity carries no password shaped property of any kind.</summary>
    [Fact]
    public void The_database_entity_carries_no_password_shaped_property_of_any_kind()
    {
        // Structural rather than a spot check, and asserted at the ENTITY so that a column added by
        // any route — plaintext, encrypted, hashed — fails here. The panel mints a password, shows
        // it once and forgets it; a stored copy is a copy that can be read out of the panel's
        // database, and a panel that can read back every customer's database password is a single
        // theft away from every customer's data.
        var suspicious = typeof(Database).GetProperties()
            .Select(property =>
            {
                return property.Name;
            })
            .Where(name =>
            {
                return SecretShapedWords.Any(word =>
                {
                    return name.Contains(word, StringComparison.OrdinalIgnoreCase);
                });
            })
            .ToList();

        Assert.True(
            suspicious.Count == 0,
            "Database must hold no password of any kind — not plaintext, not encrypted, not a hash: "
            + string.Join(", ", suspicious));
    }

    /// <summary>The mapped columns carry no password shaped name either.</summary>
    [Fact]
    public void The_mapped_columns_carry_no_password_shaped_name_either()
    {
        // The entity check above would miss a shadow property, which EF can map without any C#
        // property to name it.
        using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin());
        var columns = context.Model.FindEntityType(typeof(Database))!
            .GetProperties()
            .Select(property =>
            {
                return property.Name;
            })
            .ToList();

        Assert.NotEmpty(columns);
        Assert.DoesNotContain(
            columns,
            name =>
            {
                return SecretShapedWords.Any(word =>
                {
                    return name.Contains(word, StringComparison.OrdinalIgnoreCase);
                });
            });
    }

    /// <summary>Seeds one row for each account into a fresh shared store.</summary>
    /// <returns>The name of the store the tests open.</returns>
    private static async Task<string> SeedAsync()
    {
        var store = Guid.NewGuid().ToString();

        using var seed = DatabasesTestContext.Create(FakeCurrentUser.Admin(), store);
        seed.Databases.Add(DatabasesTestContext.Row(OwnerAccountId, "alice", "shop"));
        seed.Databases.Add(DatabasesTestContext.Row(StrangerAccountId, "bob", "ledger"));
        await seed.SaveChangesAsync();

        return store;
    }
}
