using Maran.Modules.Databases.Queries.CountDatabases;
using Maran.Modules.Databases.Tests.TestSupport;

namespace Maran.Modules.Databases.Tests.Queries.CountDatabases;

/// <summary>Behavioral contract of <see cref="CountDatabasesQueryHandler"/>.</summary>
public sealed class CountDatabasesQueryHandlerTests
{
    /// <summary>The account the customer in these tests owns.</summary>
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>Another tenant's account.</summary>
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>A customer's count is of their own databases only.</summary>
    [Fact]
    public async Task A_customers_count_is_of_their_own_databases_only()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new CountDatabasesQueryHandler(context)
            .HandleAsync(new CountDatabasesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
    }

    /// <summary>An administrator's count is of every database on the server.</summary>
    [Fact]
    public async Task An_administrators_count_is_of_every_database_on_the_server()
    {
        // The inverse control for the test above: the seed holds three rows, so the customer's two is
        // the tenant filter and not a seed that wrote two.
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await new CountDatabasesQueryHandler(context)
            .HandleAsync(new CountDatabasesQuery(), CancellationToken.None);

        Assert.Equal(3, result.Value);
    }

    /// <summary>Seeds two databases for the owner and one for the stranger into a shared store.</summary>
    /// <param name="databaseName">The shared in-memory database.</param>
    private static async Task SeedAsync(string databaseName)
    {
        using var seed = DatabasesTestContext.Create(FakeCurrentUser.Admin(), databaseName);
        seed.Databases.AddRange(
            DatabasesTestContext.Row(OwnerAccountId, "alice", "shop"),
            DatabasesTestContext.Row(OwnerAccountId, "alice", "blog"),
            DatabasesTestContext.Row(StrangerAccountId, "bob", "ledger"));
        await seed.SaveChangesAsync();
    }
}
