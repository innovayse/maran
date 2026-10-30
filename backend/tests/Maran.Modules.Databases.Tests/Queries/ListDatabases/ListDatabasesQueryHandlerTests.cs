using Maran.Agent.Client.Interfaces;
using Maran.Modules.Databases.Common;
using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Databases.Queries.ListDatabases;
using Maran.Modules.Databases.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Pagination;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Databases.Tests.Queries.ListDatabases;

/// <summary>Where a listing's rows come from, and — just as importantly — where they do not.</summary>
/// <remarks>
/// UNOBSERVED HERE: the search. It is an <c>ILIKE</c>, which only PostgreSQL translates, so the
/// InMemory provider cannot run it; <c>DatabaseListEndpointTests</c> covers it against a real server.
/// </remarks>
public sealed class ListDatabasesQueryHandlerTests
{
    /// <summary>The instant the ordered rows are created at unless a test spreads them out.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

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
        var result = await new ListDatabasesQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListDatabasesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["shop"], result.Value.Items.Select(row =>
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

    /// <summary>
    /// The reads that MAY hold the agent, each with the reason, so that everything else is refused by
    /// default and a new tenant-scoped read cannot quietly join them.
    /// </summary>
    /// <remarks>
    /// The default matters more than the entries. The law below exists because the database server has
    /// no notion of a tenant, so an answer derived from its names is derived from a prefix scan — and
    /// <c>alice_</c> is a prefix of account <c>alice_bob</c>'s names too. An entry here is a read whose
    /// subject is NOT a customer's databases, so there is no tenant for a prefix to be mistaken for.
    /// <see cref="Every_excused_reader_still_names_a_type_that_exists"/> refuses an entry that has gone
    /// stale, which is what keeps this register from rotting into a place a real tenant-scoped read
    /// hides.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> ReadersThatMayHoldTheAgent =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["InspectDatabaseGrantsQueryHandler"] =
                "Its subject is the database server's own grant table, host-wide, which the panel keeps "
                + "no copy of and which no account owns. It decodes no prefix and reports no account: "
                + "the endpoint behind it is AdminOnly precisely because the answer spans every tenant, "
                + "so there is no tenant scope here for a prefix scan to be mistaken for.",
        };

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
            .Where(name =>
            {
                return !ReadersThatMayHoldTheAgent.ContainsKey(name);
            })
            .ToList();

        Assert.True(
            readers.Count == 0,
            "A read in this module must be answered from the panel's own rows, never from the agent's "
            + "diagnostic listing: " + string.Join(", ", readers));
    }

    /// <summary>Every excused reader still names a type that exists.</summary>
    /// <remarks>
    /// An exemption for a handler that has been renamed or removed is worse than none: it is a hole in
    /// the default, sitting under a reason that reads as considered.
    /// </remarks>
    [Fact]
    public void Every_excused_reader_still_names_a_type_that_exists()
    {
        var declared = typeof(ListDatabasesQueryHandler).Assembly.GetTypes()
            .Select(type =>
            {
                return type.Name;
            })
            .ToHashSet(StringComparer.Ordinal);

        var stale = ReadersThatMayHoldTheAgent.Keys.Where(name =>
        {
            return !declared.Contains(name);
        }).ToList();

        Assert.True(
            stale.Count == 0,
            "These readers are excused from the no-agent law and no longer exist, so the excuse is now a "
            + "place a tenant-scoped read could hide: " + string.Join(", ", stale));
    }

    /// <summary>A listing shows a customer only their own rows.</summary>
    [Fact]
    public async Task A_listing_shows_a_customer_only_their_own_rows()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Customer(StrangerAccountId), shared);
        var result = await new ListDatabasesQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListDatabasesQuery(), CancellationToken.None);

        Assert.Equal(["ledger"], result.Value.Items.Select(row =>
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
        var result = await new ListDatabasesQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListDatabasesQuery(), CancellationToken.None);

        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal(2, result.Value.Total);
    }

    /// <summary>A listing carries the fully qualified names an application needs and no password.</summary>
    [Fact]
    public async Task A_listing_carries_the_fully_qualified_names_an_application_needs_and_no_password()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new ListDatabasesQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListDatabasesQuery(), CancellationToken.None);

        var row = Assert.Single(result.Value.Items);
        Assert.Equal("alice_shop", row.FullName);
        Assert.Equal("alice_shop", row.DbUserName);

        // Structural, not a spot check: the DTO must have no password-shaped member at all.
        Assert.DoesNotContain(
            typeof(DatabaseDto).GetProperties(),
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

    /// <summary>An administrator naming one account is listed only that account's databases.</summary>
    /// <remarks>
    /// Run as an administrator, whom no tenant filter narrows, so the other account's row is visible
    /// to the context and only the account filter can remove it.
    /// </remarks>
    [Fact]
    public async Task An_administrator_naming_one_account_is_listed_only_that_accounts_databases()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await new ListDatabasesQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListDatabasesQuery(AccountId: StrangerAccountId), CancellationToken.None);

        Assert.Equal(["ledger"], result.Value.Items.Select(row =>
        {
            return row.Name;
        }));
    }

    /// <summary>A customer naming another account is listed nothing, with a total of zero.</summary>
    /// <remarks>
    /// The account filter is ANDed with the tenant filter, never in place of it: the customer's own
    /// row exists and is not returned either, because it is not the account they named.
    /// </remarks>
    [Fact]
    public async Task A_customer_naming_another_account_is_listed_nothing_with_a_total_of_zero()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Customer(OwnerAccountId), shared);
        var result = await new ListDatabasesQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(new ListDatabasesQuery(AccountId: StrangerAccountId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.Total);
    }

    /// <summary>
    /// A listing labels each row with its owner's name from the account directory, in one batched
    /// lookup, and leaves the name empty for an account the directory does not name.
    /// </summary>
    [Fact]
    public async Task Rows_carry_the_owner_name_the_directory_answers_and_empty_for_one_it_does_not()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        // The directory names the owner and not the stranger — exactly how the real one answers for
        // an account that was deleted or that the caller may not see.
        var accounts = new StubAccountDirectory(
            new AccountSnapshot(OwnerAccountId, "alice", 5, 5, 5, 5, 5, 1_024, AccountState.Active));

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await new ListDatabasesQueryHandler(context, accounts)
            .HandleAsync(new ListDatabasesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("alice", result.Value.Items.Single(row =>
        {
            return row.AccountId == OwnerAccountId;
        }).OwnerName);
        Assert.Equal(string.Empty, result.Value.Items.Single(row =>
        {
            return row.AccountId == StrangerAccountId;
        }).OwnerName);

        var batch = Assert.Single(accounts.NameLookups);
        Assert.Equal(
            new[] { OwnerAccountId, StrangerAccountId }.Order(),
            batch.Order());
    }

    /// <summary>With no sort named, databases are listed oldest first.</summary>
    [Fact]
    public async Task With_no_sort_named_databases_are_listed_oldest_first()
    {
        // Inserted out of order so the assertion cannot pass by insertion-order accident.
        var shared = Guid.NewGuid().ToString();
        await SeedRowsAsync(
            shared,
            RowAt("second", Epoch.AddDays(1)),
            RowAt("first", Epoch),
            RowAt("third", Epoch.AddDays(2)));

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await HandleAsync(context, new ListDatabasesQuery());

        Assert.Equal(["first", "second", "third"], Names(result.Value.Items));
    }

    /// <summary>A descending name sort orders the page by the full name, last first.</summary>
    [Fact]
    public async Task A_descending_name_sort_orders_the_page_by_the_full_name_last_first()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedRowsAsync(shared, RowAt("bravo", Epoch), RowAt("charlie", Epoch), RowAt("alpha", Epoch));

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await HandleAsync(
            context,
            new ListDatabasesQuery(Sort: SortRequest.Parse("-" + DatabaseSortFields.Name)));

        Assert.Equal(["charlie", "bravo", "alpha"], Names(result.Value.Items));
    }

    /// <summary>The second page holds the rows after the first, and the total counts every match.</summary>
    [Fact]
    public async Task The_second_page_holds_the_rows_after_the_first_and_the_total_counts_every_match()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedRowsAsync(shared, [.. Enumerable.Range(0, 12).Select(day =>
        {
            return RowAt($"db{day:D2}", Epoch.AddDays(day));
        })]);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await HandleAsync(context, new ListDatabasesQuery(Page: 2, PageSize: 10));

        Assert.Equal(12, result.Value.Total);
        Assert.Equal(2, result.Value.Page);
        Assert.Equal(10, result.Value.PageSize);
        Assert.Equal(["db10", "db11"], Names(result.Value.Items));
    }

    /// <summary>A page past the last one answers empty with the true total and the page asked for.</summary>
    [Fact]
    public async Task A_page_past_the_last_one_answers_empty_with_the_true_total_and_the_page_asked_for()
    {
        var shared = Guid.NewGuid().ToString();
        await SeedAsync(shared);

        using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin(), shared);
        var result = await HandleAsync(context, new ListDatabasesQuery(Page: 5, PageSize: 10));

        Assert.Empty(result.Value.Items);
        Assert.Equal(2, result.Value.Total);
        Assert.Equal(5, result.Value.Page);
    }

    /// <summary>An empty page names no account to the directory.</summary>
    [Fact]
    public async Task An_empty_page_names_no_account_to_the_directory()
    {
        using var context = DatabasesTestContext.Create(FakeCurrentUser.Admin());
        var directory = new StubAccountDirectory();

        var result = await new ListDatabasesQueryHandler(context, directory)
            .HandleAsync(new ListDatabasesQuery(), CancellationToken.None);

        Assert.Empty(result.Value.Items);
        Assert.Empty(Assert.Single(directory.NameLookups));
    }

    /// <summary>Runs the handler with a directory that knows no account.</summary>
    /// <param name="context">The context to read through.</param>
    /// <param name="query">The list request.</param>
    private static Task<Result<PagedResult<DatabaseDto>>> HandleAsync(
        DatabasesDbContext context,
        ListDatabasesQuery query)
    {
        return new ListDatabasesQueryHandler(context, new StubAccountDirectory())
            .HandleAsync(query, CancellationToken.None);
    }

    /// <summary>The customer-facing names of a page, in page order.</summary>
    /// <param name="items">The page's rows.</param>
    private static List<string> Names(IReadOnlyList<DatabaseDto> items)
    {
        return items
            .Select(database =>
            {
                return database.Name;
            })
            .ToList();
    }

    /// <summary>Builds one of the owner's databases created at <paramref name="createdAt"/>.</summary>
    /// <param name="name">The name the customer asked for.</param>
    /// <param name="createdAt">The creation instant.</param>
    private static Database RowAt(string name, DateTimeOffset createdAt)
    {
        return new Database(
            Guid.NewGuid(), OwnerAccountId, name, $"alice_{name}", $"alice_{name}", name, createdAt);
    }

    /// <summary>Writes the given rows into one store, as an administrator.</summary>
    /// <param name="databaseName">The shared in-memory database.</param>
    /// <param name="rows">The rows to write.</param>
    private static async Task SeedRowsAsync(string databaseName, params Database[] rows)
    {
        using var seed = DatabasesTestContext.Create(FakeCurrentUser.Admin(), databaseName);
        seed.Databases.AddRange(rows);
        await seed.SaveChangesAsync();
    }
}
