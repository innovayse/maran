using Maran.Agent.Client.Interfaces;
using Maran.Modules.Databases.Queries.ListDatabases;
using Maran.Modules.Databases.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Databases.Tests.Queries.ListDatabases;

/// <summary>Where a listing's rows come from, and — just as importantly — where they do not.</summary>
public sealed class ListDatabasesQueryHandlerTests
{
    private static readonly Guid OwnerAccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid StrangerAccountId = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>Listing databases returns the panels own tenant rows and never asks the agent to enumerate.</summary>
    [Fact]
    public async Task Listing_databases_returns_the_panels_own_tenant_rows_and_never_asks_the_agent_to_enumerate()
    {
        // The plan's central correction. The MySQL server has no notion of a tenant, so an answer
        // derived from its names is derived from a prefix scan — and `alice_` is a prefix of account
        // `alice_bob`'s names too, because account names may contain the separator. Authorisation is
        // the panel's own tenant-filtered rows, and the agent's enumerate is diagnostic only.
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new ListDatabasesQueryHandler(context)
            .HandleAsync(new ListDatabasesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["shop"], result.Value.Select(row =>
        {
            return row.Name;
        }));

        // Asserted STRUCTURALLY, over the type rather than over a call counter on a double the
        // handler was never given. A counter on an unwired double reads as proof and is not: it
        // stays at zero however the handler is written, so it could never have gone red. What can go
        // red is this — the handler cannot ask the agent anything, because it cannot be handed one.
        Assert.DoesNotContain(
            typeof(ListDatabasesQueryHandler).GetConstructors()
                .SelectMany(constructor =>
                {
                    return constructor.GetParameters();
                }),
            parameter =>
            {
                return parameter.ParameterType == typeof(IAgentDbClient);
            });
    }

    /// <summary>No query handler in this module can be handed the agent at all.</summary>
    [Fact]
    public void No_query_handler_in_this_module_can_be_handed_the_agent_at_all()
    {
        // The same guarantee widened past the one handler the test above names, so a NEW read that
        // reached for the agent's diagnostic enumerate would fail here rather than ship. Reads in
        // this module are answered from the panel's tenant-filtered rows and from nothing else: the
        // server has no notion of a tenant, so any answer derived from its names is derived from a
        // prefix scan, and `alice_` is a prefix of account `alice_bob`'s names too.
        var readers = typeof(ListDatabasesQueryHandler).Assembly.GetTypes()
            .Where(type =>
            {
                return type.Namespace?.StartsWith(
                    "Maran.Modules.Databases.Queries", StringComparison.Ordinal) == true;
            })
            .Where(type =>
            {
                return type.GetConstructors().SelectMany(constructor =>
                {
                    return constructor.GetParameters();
                }).Any(parameter =>
                {
                    return parameter.ParameterType == typeof(IAgentDbClient);
                });
            })
            .Select(type =>
            {
                return type.Name;
            })
            .ToList();

        Assert.True(
            readers.Count == 0,
            "A read in this module must be answered from the panel's own rows, never from the agent's "
            + "diagnostic listing: " + string.Join(", ", readers));
    }

    /// <summary>A listing shows a customer only their own rows.</summary>
    [Fact]
    public async Task A_listing_shows_a_customer_only_their_own_rows()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Customer(StrangerAccountId), shared);
        var result = await new ListDatabasesQueryHandler(context)
            .HandleAsync(new ListDatabasesQuery(), CancellationToken.None);

        Assert.Equal(["ledger"], result.Value.Select(row =>
        {
            return row.Name;
        }));
    }

    /// <summary>An administrator sees every accounts rows.</summary>
    [Fact]
    public async Task An_administrator_sees_every_accounts_rows()
    {
        // Guards the two tests above from passing for the wrong reason: if the seed never ran, or the
        // handler simply returned nothing, "only their own" would be true of an empty result.
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await new ListDatabasesQueryHandler(context)
            .HandleAsync(new ListDatabasesQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value.Count);
    }

    /// <summary>A listing carries the fully qualified names an application needs and no password.</summary>
    [Fact]
    public async Task A_listing_carries_the_fully_qualified_names_an_application_needs_and_no_password()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new ListDatabasesQueryHandler(context)
            .HandleAsync(new ListDatabasesQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value);
        Assert.Equal("alice_shop", row.FullName);
        Assert.Equal("alice_shop", row.DbUserName);

        // Structural, not a spot check: the DTO must have no password-shaped member at all.
        Assert.DoesNotContain(
            typeof(Modules.Databases.Common.DatabaseDto).GetProperties(),
            property =>
            {
                return property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>Seeds one database for each of the two accounts into a shared store.</summary>
    /// <param name="databaseName">The shared in-memory database.</param>
    private static async Task SeedAsync(string databaseName)
    {
        // Written through a context whose principal is an administrator, so the seed itself does no
        // tenant separating — the filter under test is the only thing that can.
        using var seed = DatabasesTestContext.Create(FakeCurrentUser.Admin(), databaseName);
        seed.Databases.Add(DatabasesTestContext.Row(OwnerAccountId, "alice", "shop"));
        seed.Databases.Add(DatabasesTestContext.Row(StrangerAccountId, "bob", "ledger"));
        await seed.SaveChangesAsync();
    }
}
