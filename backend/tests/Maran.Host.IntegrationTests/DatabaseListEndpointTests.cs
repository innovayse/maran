using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The paged database list (<c>GET /api/v1/databases</c>) over real HTTP against a real PostgreSQL:
/// the search is an <c>ILIKE</c> only PostgreSQL runs, the tenant filter scopes the total as well as
/// the page, and the paging bounds are refused by the validator before the handler is reached.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class DatabaseListEndpointTests : IAsyncLifetime
{
    /// <summary>The creation instant the seeded databases share unless a test spreads them out.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public DatabaseListEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// A search made of LIKE wildcards, a backslash, a quote and SQL matches literally, so it matches
    /// nothing — while <c>r_l</c> matches only the name that holds it and not <c>carel</c>, which an
    /// unescaped underscore would also match: the search ran and the wildcard was escaped rather than
    /// honoured. (A bare underscore would prove nothing here: every full name holds the prefix's one.)
    /// </summary>
    [Fact]
    public async Task A_search_of_wildcards_quotes_and_sql_matches_literally()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        await SeedDatabasesAsync(factory, owner, "owner", ["alpha", "bravo", "char_lie", "carel"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var hostile = await GetJsonAsync(admin, "/api/v1/databases?search=" + Uri.EscapeDataString("%\\'; drop"));
        using var underscore = await GetJsonAsync(admin, "/api/v1/databases?search=" + Uri.EscapeDataString("r_l"));
        using var anyCase = await GetJsonAsync(admin, "/api/v1/databases?search=LPH");

        Assert.Equal(0, hostile.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["owner_char_lie"], FullNames(underscore));
        Assert.Equal(["owner_alpha"], FullNames(anyCase));
    }

    /// <summary>
    /// The search runs over the full name, so the account's prefix finds every database of that
    /// account, and each row carries the owner's name the server resolved.
    /// </summary>
    [Fact]
    public async Task The_search_reaches_the_prefix_and_each_row_carries_the_owners_name()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var olive = await SeedWorldAsync(factory, "olive");
        var (_, pepper) = await EndpointHarness.SeedAccountAsync(factory, "pepper");
        await SeedDatabasesAsync(factory, olive, "olive", ["one", "two"]);
        await SeedDatabasesAsync(factory, pepper, "pepper", ["three"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var page = await GetJsonAsync(admin, "/api/v1/databases?search=LIV&sort=name");

        Assert.Equal(2, page.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["olive_one", "olive_two"], FullNames(page));
        Assert.All(page.RootElement.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.Equal("olive", item.GetProperty("ownerName").GetString());
        });
    }

    /// <summary>
    /// The IDOR case: a customer searching for another tenant's database, or naming that tenant's
    /// account, is answered an empty page with a total of zero — the total discloses nothing either.
    /// </summary>
    /// <remarks>
    /// The administrator's answer to the same requests is asserted first, so the zero is the tenant
    /// scope and not a search that matched nothing for anybody.
    /// </remarks>
    [Fact]
    public async Task A_customer_searching_or_naming_another_tenant_gets_a_total_of_zero()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var own = await SeedWorldAsync(factory, "own");
        var (_, stranger) = await EndpointHarness.SeedAccountAsync(factory, "stranger");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", own);
        await SeedDatabasesAsync(factory, own, "own", ["shop"]);
        await SeedDatabasesAsync(factory, stranger, "stranger", ["secret"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");

        using var adminBySearch = await GetJsonAsync(admin, "/api/v1/databases?search=secret");
        using var adminByAccount = await GetJsonAsync(admin, $"/api/v1/databases?accountId={stranger}");
        using var bySearch = await GetJsonAsync(customer, "/api/v1/databases?search=secret");
        using var byAccount = await GetJsonAsync(customer, $"/api/v1/databases?accountId={stranger}");
        using var mine = await GetJsonAsync(customer, "/api/v1/databases");

        Assert.Equal(1, adminBySearch.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, adminByAccount.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, bySearch.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, bySearch.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, byAccount.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, byAccount.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(["own_shop"], FullNames(mine));
        Assert.Equal(1, mine.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// The total counts every database, the last page holds the remainder, and a page past it is empty
    /// with the same total.
    /// </summary>
    [Fact]
    public async Task The_total_the_last_page_and_a_page_past_it_answer_as_the_contract_says()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        await SeedDatabasesAsync(
            factory,
            owner,
            "owner",
            [.. Enumerable.Range(0, 12).Select(index => { return $"db{index:D2}"; })],
            spread: true);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var last = await GetJsonAsync(admin, "/api/v1/databases?page=2&pageSize=10");
        using var beyond = await GetJsonAsync(admin, "/api/v1/databases?page=3&pageSize=10");

        Assert.Equal(12, last.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["owner_db10", "owner_db11"], FullNames(last));
        Assert.Equal(0, beyond.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(12, beyond.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(3, beyond.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(10, beyond.RootElement.GetProperty("pageSize").GetInt32());
    }

    /// <summary>Databases sharing one creation instant page stably: the pages are disjoint and cover every row.</summary>
    [Fact]
    public async Task Databases_sharing_one_creation_instant_page_stably()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        await SeedDatabasesAsync(
            factory,
            owner,
            "owner",
            [.. Enumerable.Range(0, 15).Select(index => { return $"tie{index:D2}"; })]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var first = await GetJsonAsync(admin, "/api/v1/databases?page=1&pageSize=10");
        using var second = await GetJsonAsync(admin, "/api/v1/databases?page=2&pageSize=10");
        using var again = await GetJsonAsync(admin, "/api/v1/databases?page=1&pageSize=10");

        var firstNames = FullNames(first);
        var secondNames = FullNames(second);
        Assert.Equal(firstNames, FullNames(again));
        Assert.Empty(firstNames.Intersect(secondNames));
        Assert.Equal(15, firstNames.Concat(secondNames).Distinct().Count());
    }

    /// <summary>
    /// A bound the list refuses answers 400 with its code: page 0, an unoffered size, an unknown sort,
    /// a malformed sort.
    /// </summary>
    /// <param name="queryString">The query string sent.</param>
    /// <param name="code">The machine code the refusal must carry.</param>
    [Theory]
    [InlineData("page=0", "ListPageOutOfRange")]
    [InlineData("pageSize=101", "ListPageSizeNotOffered")]
    [InlineData("sort=dbUserName", "ListSortUnknown")]
    [InlineData("sort=-", "ListSortUnknown")]
    public async Task A_bound_the_list_refuses_answers_400_with_its_code(string queryString, string code)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory, "owner");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/databases?" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeOfAsync(response));
    }

    /// <summary>A search longer than the contract allows is refused while binding, with its code.</summary>
    [Fact]
    public async Task A_search_longer_than_the_contract_allows_is_refused_with_its_code()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory, "owner");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/databases?search=" + new string('a', 101));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ListSearchTooLong", await CodeOfAsync(response));
    }

    /// <summary>
    /// Every sort key translates to an ORDER BY PostgreSQL runs — a string (name) and a timestamp
    /// (createdAt) — and the descending order is the ascending one reversed. Each key is unique across
    /// the two rows, and the two keys disagree, so the order is the key's and not the tie-breaker's.
    /// </summary>
    /// <param name="field">The sort key.</param>
    /// <param name="expected">The full names in ascending order of that key.</param>
    [Theory]
    [InlineData("name", new[] { "owner_a", "owner_b" })]
    [InlineData("createdAt", new[] { "owner_b", "owner_a" })]
    public async Task Every_sort_key_orders_on_postgresql_in_both_directions(string field, string[] expected)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        using (var scope = factory.Services.CreateScope())
        {
            var databases = scope.ServiceProvider.GetRequiredService<DatabasesDbContext>();
            databases.Databases.AddRange(
                NewDatabase(owner, "owner", "a", Epoch.AddDays(1)),
                NewDatabase(owner, "owner", "b", Epoch));
            await databases.SaveChangesAsync();
        }

        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var ascending = await GetJsonAsync(admin, "/api/v1/databases?sort=" + field);
        using var descending = await GetJsonAsync(admin, "/api/v1/databases?sort=-" + field);

        Assert.Equal(expected, FullNames(ascending));
        Assert.Equal(expected.Reverse(), FullNames(descending));
    }

    /// <summary>Migrates, seeds the administrator, and stores one account, returning its id.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account to store.</param>
    private static async Task<Guid> SeedWorldAsync(WebApplicationFactory<Program> factory, string accountName)
    {
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabasesDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, accountName);
        return accountId;
    }

    /// <summary>Stores one database per name for the account.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="username">The account's name, which forms the prefix.</param>
    /// <param name="names">The names the customer asked for.</param>
    /// <param name="spread">
    /// True to create each database a day after the one before, in the order given; false to give them
    /// all one creation instant.
    /// </param>
    private static async Task SeedDatabasesAsync(
        WebApplicationFactory<Program> factory,
        Guid accountId,
        string username,
        string[] names,
        bool spread = false)
    {
        // Written through a context resolved outside a request, so no tenant filter separates the
        // seed: the filter under test is the only thing that can.
        using var scope = factory.Services.CreateScope();
        var databases = scope.ServiceProvider.GetRequiredService<DatabasesDbContext>();
        for (var index = 0; index < names.Length; index++)
        {
            databases.Databases.Add(NewDatabase(accountId, username, names[index], spread ? Epoch.AddDays(index) : Epoch));
        }

        await databases.SaveChangesAsync();
    }

    /// <summary>Builds one database row under <paramref name="username"/>'s prefix.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="username">The account's name, which forms the prefix.</param>
    /// <param name="name">The name the customer asked for.</param>
    /// <param name="createdAt">The creation instant.</param>
    private static Database NewDatabase(Guid accountId, string username, string name, DateTimeOffset createdAt)
    {
        return new Database(
            Guid.NewGuid(), accountId, name, $"{username}_{name}", $"{username}_{name}", name, createdAt);
    }

    /// <summary>Reads a successful JSON answer.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="path">The path and query string.</param>
    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>The full names on a page, in order.</summary>
    /// <param name="page">The page's JSON.</param>
    private static List<string> FullNames(JsonDocument page)
    {
        return page.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => { return item.GetProperty("fullName").GetString()!; })
            .ToList();
    }

    /// <summary>Reads the machine code of a problem answer.</summary>
    /// <param name="response">The refused response.</param>
    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString()!;
    }
}
