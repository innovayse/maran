using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.SharedKernel.Pagination;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The paged account list (<c>GET /api/v1/accounts</c>) and the account picker
/// (<c>GET /api/v1/accounts/choices</c>) over real HTTP against a real PostgreSQL: the search is an
/// <c>ILIKE</c> only PostgreSQL runs, and the paging bounds are refused by the validator before the
/// handler is reached.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class AccountListEndpointTests : IAsyncLifetime
{
    /// <summary>The creation instant the seeded accounts share unless a test spreads them out.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountListEndpointTests(PostgresFixture postgres)
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
    /// A search made of LIKE wildcards, a quote and SQL matches literally, so it matches nothing —
    /// while an underscore matches only the account that holds one, which shows the search ran and
    /// that the wildcard was escaped rather than honoured.
    /// </summary>
    [Fact]
    public async Task A_search_of_wildcards_quotes_and_sql_matches_literally()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["alpha", "bravo", "char_lie"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var hostile = await GetJsonAsync(admin, "/api/v1/accounts?search=" + Uri.EscapeDataString("%_\\'; drop"));
        using var underscore = await GetJsonAsync(admin, "/api/v1/accounts?search=_");
        using var anyCase = await GetJsonAsync(admin, "/api/v1/accounts?search=LPH");

        Assert.Equal(0, hostile.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["char_lie"], Names(underscore));
        Assert.Equal(["alpha"], Names(anyCase));
    }

    /// <summary>The search reaches the primary domain as well as the name.</summary>
    [Fact]
    public async Task The_search_reaches_the_primary_domain_as_well_as_the_name()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["alpha", "bravo"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var page = await GetJsonAsync(admin, "/api/v1/accounts?search=" + Uri.EscapeDataString("bravo.example"));

        Assert.Equal(["bravo"], Names(page));
    }

    /// <summary>The total counts every account, the last page holds the remainder, and a page past it is empty with the same total.</summary>
    [Fact]
    public async Task The_total_the_last_page_and_a_page_past_it_answer_as_the_contract_says()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, Enumerable.Range(0, 12).Select(index => { return $"acct{index:D2}"; }).ToArray(), spread: true);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var last = await GetJsonAsync(admin, "/api/v1/accounts?page=2&pageSize=10");
        using var beyond = await GetJsonAsync(admin, "/api/v1/accounts?page=3&pageSize=10");

        Assert.Equal(12, last.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["acct10", "acct11"], Names(last));
        Assert.Equal(0, beyond.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(12, beyond.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(3, beyond.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(10, beyond.RootElement.GetProperty("pageSize").GetInt32());
    }

    /// <summary>
    /// Accounts sharing one creation instant page stably: the two pages are disjoint, cover every
    /// account, and the first page reads the same twice.
    /// </summary>
    [Fact]
    public async Task Accounts_sharing_one_creation_instant_page_stably()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, Enumerable.Range(0, 15).Select(index => { return $"tie{index:D2}"; }).ToArray());
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var first = await GetJsonAsync(admin, "/api/v1/accounts?page=1&pageSize=10");
        using var second = await GetJsonAsync(admin, "/api/v1/accounts?page=2&pageSize=10");
        using var again = await GetJsonAsync(admin, "/api/v1/accounts?page=1&pageSize=10");

        var firstNames = Names(first);
        var secondNames = Names(second);
        Assert.Equal(firstNames, Names(again));
        Assert.Empty(firstNames.Intersect(secondNames));
        Assert.Equal(15, firstNames.Concat(secondNames).Distinct().Count());
    }

    /// <summary>A sort the list does not expose is refused with 400 and its code.</summary>
    [Fact]
    public async Task A_sort_the_list_does_not_expose_is_refused_with_its_code()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["alpha"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/accounts?sort=ownerEmail");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ListSortUnknown", await CodeOfAsync(response));
    }

    /// <summary>
    /// An absent, an empty and a whitespace-only search all bind to "no search": the same total,
    /// whoever the client is and whether it omits the parameter or not.
    /// </summary>
    /// <param name="queryString">The search as a client might send it.</param>
    [Theory]
    [InlineData("")]
    [InlineData("search=")]
    [InlineData("search=%20%20%20")]
    public async Task An_absent_empty_or_blank_search_binds_to_no_search(string queryString)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["alpha", "bravo", "charlie"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var page = await GetJsonAsync(admin, "/api/v1/accounts?" + queryString);

        Assert.Equal(3, page.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// A search or a sort the binder cannot accept answers 400 with its own code: a control
    /// character in the search, and a sort that is not a well-formed key.
    /// </summary>
    /// <param name="queryString">The refused parameter.</param>
    /// <param name="code">The code it answers.</param>
    [Theory]
    [InlineData("search=al%0Apha", "ListSearchInvalid")]
    [InlineData("sort=name%3Bdrop", "ListSortUnknown")]
    public async Task A_search_or_sort_the_binder_refuses_answers_400_with_its_code(string queryString, string code)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["alpha"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/accounts?" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeOfAsync(response));
    }

    /// <summary>A search one character over the limit answers 400 with its own code.</summary>
    [Fact]
    public async Task A_search_over_the_limit_answers_400_with_its_code()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["alpha"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync(
            "/api/v1/accounts?search=" + new string('a', PageDefaults.SearchMaxLength + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ListSearchTooLong", await CodeOfAsync(response));
    }

    /// <summary>
    /// Every sort key translates to an ORDER BY PostgreSQL runs — a string (name), a timestamp
    /// (createdAt) and an enum (status) — and the descending order is the ascending one reversed.
    /// Each key is unique across the seeded rows, so the order is the key's alone and not the
    /// tie-breaker's.
    /// </summary>
    /// <param name="field">The sort key.</param>
    /// <param name="expected">The names in ascending order of that key.</param>
    [Theory]
    [InlineData("name", new[] { "alpha", "bravo" })]
    [InlineData("createdAt", new[] { "bravo", "alpha" })]
    [InlineData("status", new[] { "bravo", "alpha" })]
    public async Task Every_sort_key_orders_on_postgresql_in_both_directions(string field, string[] expected)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["bravo", "alpha"], spread: true);
        using (var scope = factory.Services.CreateScope())
        {
            // Active orders before Suspended, so suspending alpha makes the status order bravo, alpha.
            var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            (await accounts.Accounts.SingleAsync(account => account.Name == "alpha")).Suspend();
            await accounts.SaveChangesAsync();
        }

        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var ascending = await GetJsonAsync(admin, "/api/v1/accounts?sort=" + field);
        using var descending = await GetJsonAsync(admin, "/api/v1/accounts?sort=-" + field);

        Assert.Equal(expected, Names(ascending));
        Assert.Equal(expected.Reverse(), Names(descending));
    }

    /// <summary>A page size the screens do not offer is refused with 400 and its code, never clamped.</summary>
    [Fact]
    public async Task A_page_size_the_screens_do_not_offer_is_refused_with_its_code()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["alpha"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/accounts?pageSize=7");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ListPageSizeNotOffered", await CodeOfAsync(response));
    }

    /// <summary>
    /// The status filter keeps only accounts in that state, written in the camelCase the JSON answers
    /// with and the SPA sends.
    /// </summary>
    [Fact]
    public async Task The_status_filter_keeps_only_accounts_in_that_state()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["alpha", "bravo"]);
        using (var scope = factory.Services.CreateScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
            (await accounts.Accounts.SingleAsync(account => account.Name == "bravo")).Suspend();
            await accounts.SaveChangesAsync();
        }

        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var page = await GetJsonAsync(admin, "/api/v1/accounts?status=suspended");

        Assert.Equal(["bravo"], Names(page));
    }

    /// <summary>The picker answers id and name only, searched by name, alphabetical, with the total.</summary>
    [Fact]
    public async Task The_picker_answers_id_and_name_only_searched_by_name()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["olive", "oliver", "pepper"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var page = await GetJsonAsync(admin, "/api/v1/accounts/choices?search=oli");

        Assert.Equal(2, page.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["olive", "oliver"], Names(page));
        var choice = page.RootElement.GetProperty("items")[0];
        Assert.Equal(["id", "name"], choice.EnumerateObject().Select(property => { return property.Name; }));
    }

    /// <summary>The picker is capped by the same contract: a size above the largest offered is refused.</summary>
    [Fact]
    public async Task The_picker_is_capped_by_the_same_contract()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedAsync(factory, ["alpha"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/accounts/choices?pageSize=1000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ListPageSizeNotOffered", await CodeOfAsync(response));
    }

    /// <summary>Migrates, seeds the administrator, and stores the named accounts on one plan.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="names">The accounts to store.</param>
    /// <param name="spread">
    /// True to create each account a day after the one before, in the order given; false to give them
    /// all one creation instant.
    /// </param>
    private static async Task SeedAsync(WebApplicationFactory<Program> factory, string[] names, bool spread = false)
    {
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var plan = Plan.Define(Guid.NewGuid(), "Base", 5_120, 5, 2, 3, 0, 5, 5);
        context.Plans.Add(plan);
        for (var index = 0; index < names.Length; index++)
        {
            var createdAt = spread ? Epoch.AddDays(index) : Epoch;
            context.Accounts.Add(new Account(Guid.NewGuid(), names[index], $"{names[index]}.example.com", plan.Id, createdAt));
        }

        await context.SaveChangesAsync();
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

    /// <summary>The names on a page, in order.</summary>
    /// <param name="page">The page's JSON.</param>
    private static List<string> Names(JsonDocument page)
    {
        return page.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => { return item.GetProperty("name").GetString()!; })
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
