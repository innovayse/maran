using Maran.Modules.Databases.Queries.GetDatabase;
using Maran.Modules.Databases.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Databases.Tests.Queries.GetDatabase;

/// <summary>What one read answers for a row of one's own, and for a row of somebody else's.</summary>
public sealed class GetDatabaseQueryHandlerTests
{
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>Reading another tenants database answers not found rather than forbidden.</summary>
    [Fact]
    public async Task Reading_another_tenants_database_answers_not_found_rather_than_forbidden()
    {
        // 403 would confirm the identifier names a real database, which turns this endpoint into an
        // oracle for enumerating other customers' data. The handler makes no such distinction: the
        // query filter means the row genuinely is not in the result set.
        var shared = Guid.NewGuid().ToString();
        var strangerId = await SeedAsync(shared);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new GetDatabaseQueryHandler(context)
            .HandleAsync(new GetDatabaseQuery(strangerId), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseNotFound", result.Error!.Code);
    }

    /// <summary>An identifier that names nothing answers exactly as another tenants does.</summary>
    [Fact]
    public async Task An_identifier_that_names_nothing_answers_exactly_as_another_tenants_does()
    {
        // The two must be indistinguishable, or the difference between them IS the oracle.
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new GetDatabaseQueryHandler(context)
            .HandleAsync(new GetDatabaseQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("DatabaseNotFound", result.Error!.Code);
    }

    /// <summary>A customer reading their own database is answered with it.</summary>
    [Fact]
    public async Task A_customer_reading_their_own_database_is_answered_with_it()
    {
        // Guards the two above from passing for the wrong reason: if the read were simply broken,
        // "not found" would be true of every request.
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var read = DatabasesTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var own = await read.Databases.SingleAsync();

        var result = await new GetDatabaseQueryHandler(read)
            .HandleAsync(new GetDatabaseQuery(own.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("shop", result.Value.Name);
        Assert.Equal("alice_shop", result.Value.FullName);
    }

    /// <summary>Seeds one database for each account and returns the stranger's identifier.</summary>
    /// <param name="databaseName">The shared in-memory database.</param>
    private static async Task<Guid> SeedAsync(string databaseName)
    {
        using var seed = DatabasesTestContext.Create(FakeCurrentUser.Admin(), databaseName);
        seed.Databases.Add(DatabasesTestContext.Row(OwnerAccountId, "alice", "shop"));
        var stranger = DatabasesTestContext.Row(StrangerAccountId, "bob", "ledger");
        seed.Databases.Add(stranger);
        await seed.SaveChangesAsync();

        return stranger.Id;
    }
}
