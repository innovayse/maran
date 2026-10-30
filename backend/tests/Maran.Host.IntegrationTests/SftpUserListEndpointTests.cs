using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The paged SFTP login list (<c>GET /api/v1/sftp-users</c>) over real HTTP against a real
/// PostgreSQL: the search is an <c>ILIKE</c> only PostgreSQL runs, the tenant filter scopes the total
/// as well as the rows, and the paging bounds are refused by the validator before the handler is
/// reached.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class SftpUserListEndpointTests : IAsyncLifetime
{
    /// <summary>The path under test.</summary>
    private const string ListPath = "/api/v1/sftp-users";

    /// <summary>The creation instant the seeded logins start from, a day apart when spread.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SftpUserListEndpointTests(PostgresFixture postgres)
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
    /// nothing — while an underscore matches only the login that holds it at that place, which shows
    /// the search ran and that the wildcard was escaped rather than honoured.
    /// </summary>
    [Fact]
    public async Task A_search_of_wildcards_quotes_and_sql_matches_literally()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        await SeedLoginsAsync(factory, owner, "owner", ["ab", "xab"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var hostile = await GetJsonAsync(admin, ListPath + "?search=" + Uri.EscapeDataString("%_\\'; drop"));
        using var underscore = await GetJsonAsync(admin, ListPath + "?search=_a");
        using var anyCase = await GetJsonAsync(admin, ListPath + "?search=XAB");

        Assert.Equal(0, hostile.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["owner_ab"], FullNames(underscore));
        Assert.Equal(["owner_xab"], FullNames(anyCase));
    }

    /// <summary>
    /// The IDOR case: a customer searching for another tenant's login is answered an empty page with
    /// a total of zero, and naming that tenant's account answers the same — the total discloses
    /// nothing either.
    /// </summary>
    /// <remarks>
    /// The administrator's answer to the same search is asserted first, so the zero is the tenant
    /// scope and not a search that matched nothing for anybody.
    /// </remarks>
    [Fact]
    public async Task A_customer_searching_or_filtering_for_another_tenants_logins_gets_a_total_of_zero()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var own = await SeedWorldAsync(factory, "own");
        var (_, stranger) = await EndpointHarness.SeedAccountAsync(factory, "stranger");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", own);
        await SeedLoginsAsync(factory, own, "own", ["web"]);
        await SeedLoginsAsync(factory, stranger, "stranger", ["secret"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");

        using var adminSearch = await GetJsonAsync(admin, ListPath + "?search=secret");
        using var search = await GetJsonAsync(customer, ListPath + "?search=secret");
        using var filtered = await GetJsonAsync(customer, $"{ListPath}?accountId={stranger}");
        using var mine = await GetJsonAsync(customer, ListPath);

        Assert.Equal(1, adminSearch.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, search.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, search.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, filtered.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["own_web"], FullNames(mine));
        Assert.Equal(1, mine.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// The total counts every login, the last page holds the remainder, and a page past it is empty
    /// with the same total. Each row carries its owner's name.
    /// </summary>
    [Fact]
    public async Task The_total_the_last_page_and_a_page_past_it_answer_as_the_contract_says()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        await SeedLoginsAsync(
            factory,
            owner,
            "owner",
            [.. Enumerable.Range(0, 12).Select(index => { return $"login{index:D2}"; })]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var last = await GetJsonAsync(admin, ListPath + "?page=2&pageSize=10");
        using var beyond = await GetJsonAsync(admin, ListPath + "?page=3&pageSize=10");

        Assert.Equal(12, last.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["owner_login10", "owner_login11"], FullNames(last));
        Assert.All(last.RootElement.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.Equal("owner", item.GetProperty("ownerName").GetString());
        });
        Assert.Equal(0, beyond.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(12, beyond.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(3, beyond.RootElement.GetProperty("page").GetInt32());
    }

    /// <summary>
    /// Every sort key translates to an ORDER BY PostgreSQL runs, and the descending order is the
    /// ascending one reversed. The two keys disagree on the seeded rows, so the order is the key's.
    /// </summary>
    /// <param name="field">The sort key.</param>
    /// <param name="expected">The full names in ascending order of that key.</param>
    [Theory]
    [InlineData("name", new[] { "owner_alpha", "owner_bravo" })]
    [InlineData("createdAt", new[] { "owner_bravo", "owner_alpha" })]
    public async Task Every_sort_key_orders_on_postgresql_in_both_directions(string field, string[] expected)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        await SeedLoginsAsync(factory, owner, "owner", ["bravo", "alpha"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var ascending = await GetJsonAsync(admin, ListPath + "?sort=" + field);
        using var descending = await GetJsonAsync(admin, ListPath + "?sort=-" + field);

        Assert.Equal(expected, FullNames(ascending));
        Assert.Equal(expected.Reverse(), FullNames(descending));
    }

    /// <summary>
    /// A bound the list refuses answers 400 with its code: page 0, an unoffered size, an unknown sort.
    /// </summary>
    /// <param name="queryString">The offending query string.</param>
    /// <param name="code">The code the refusal carries.</param>
    [Theory]
    [InlineData("page=0", "ListPageOutOfRange")]
    [InlineData("pageSize=101", "ListPageSizeNotOffered")]
    [InlineData("sort=fullName", "ListSortUnknown")]
    public async Task A_bound_the_list_refuses_answers_400_with_its_code(string queryString, string code)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory, "owner");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync(ListPath + "?" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeOfAsync(response));
    }

    /// <summary>Migrates, seeds the administrator, and stores one account, returning its id.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account to store.</param>
    private static async Task<Guid> SeedWorldAsync(WebApplicationFactory<Program> factory, string accountName)
    {
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SftpDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, accountName);
        return accountId;
    }

    /// <summary>Stores one login per name for the account, each created a day after the one before.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="accountName">The account's name, which prefixes every full name.</param>
    /// <param name="names">The logins' suffixes, in creation order.</param>
    private static async Task SeedLoginsAsync(
        WebApplicationFactory<Program> factory,
        Guid accountId,
        string accountName,
        string[] names)
    {
        // Written through a context resolved outside a request, so no tenant filter separates the
        // seed: the filter under test is the only thing that can.
        using var scope = factory.Services.CreateScope();
        var sftp = scope.ServiceProvider.GetRequiredService<SftpDbContext>();
        for (var index = 0; index < names.Length; index++)
        {
            sftp.SftpUsers.Add(new SftpUser(
                Guid.NewGuid(), accountId, names[index], $"{accountName}_{names[index]}", Epoch.AddDays(index)));
        }

        await sftp.SaveChangesAsync();
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
