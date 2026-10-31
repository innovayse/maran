using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The paged site list (<c>GET /api/v1/sites</c>) over real HTTP against a real PostgreSQL: the
/// search is an <c>ILIKE</c> only PostgreSQL runs, the owner half of it crosses into the Accounts
/// module through the real <c>AccountDirectory</c>, and the paging bounds are refused by the
/// validator before the handler is reached.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class SiteListEndpointTests : IAsyncLifetime
{
    /// <summary>The creation instant the seeded sites share unless a test spreads them out.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SiteListEndpointTests(PostgresFixture postgres)
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
    /// nothing — while an underscore matches only the site that holds one, which shows the search ran
    /// and that the wildcard was escaped rather than honoured.
    /// </summary>
    [Fact]
    public async Task A_search_of_wildcards_quotes_and_sql_matches_literally()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        await SeedSitesAsync(factory, owner, ["alpha.example.com", "bravo.example.com", "char_lie.example.com"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var hostile = await GetJsonAsync(admin, "/api/v1/sites?search=" + Uri.EscapeDataString("%_\\'; drop"));
        using var underscore = await GetJsonAsync(admin, "/api/v1/sites?search=_");
        using var anyCase = await GetJsonAsync(admin, "/api/v1/sites?search=LPH");

        Assert.Equal(0, hostile.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["char_lie.example.com"], Domains(underscore));
        Assert.Equal(["alpha.example.com"], Domains(anyCase));
    }

    /// <summary>
    /// The search reaches the owner's name: a term found in no domain lists every site of the account
    /// whose name holds it, and each row carries that owner's name.
    /// </summary>
    [Fact]
    public async Task The_search_reaches_the_owners_name_and_each_row_carries_it()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var olive = await SeedWorldAsync(factory, "olive");
        var (_, pepper) = await EndpointHarness.SeedAccountAsync(factory, "pepper");
        await SeedSitesAsync(factory, olive, ["one.example.com", "two.example.com"]);
        await SeedSitesAsync(factory, pepper, ["three.example.com"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var page = await GetJsonAsync(admin, "/api/v1/sites?search=LIV&sort=domain");

        Assert.Equal(2, page.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["one.example.com", "two.example.com"], Domains(page));
        Assert.All(page.RootElement.GetProperty("items").EnumerateArray(), item =>
        {
            Assert.Equal("olive", item.GetProperty("ownerName").GetString());
        });
    }

    /// <summary>
    /// The owner search is literal too: an underscore in the term is not a wildcard over account names.
    /// </summary>
    [Fact]
    public async Task The_owner_search_is_literal_too()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "olive");
        await SeedSitesAsync(factory, owner, ["one.example.com"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var page = await GetJsonAsync(admin, "/api/v1/sites?search=" + Uri.EscapeDataString("ol_ve"));

        Assert.Equal(0, page.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// The IDOR case: a customer searching for another tenant's domain, or for that tenant's name,
    /// is answered an empty page with a total of zero — the total discloses nothing either.
    /// </summary>
    /// <remarks>
    /// The administrator's answer to the same searches is asserted first, so the zero is the tenant
    /// scope and not a search that matched nothing for anybody.
    /// </remarks>
    [Fact]
    public async Task A_customer_searching_another_tenants_domain_or_name_gets_a_total_of_zero()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var own = await SeedWorldAsync(factory, "own");
        var (_, stranger) = await EndpointHarness.SeedAccountAsync(factory, "stranger");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", own);
        await SeedSitesAsync(factory, own, ["own.example.com"]);
        await SeedSitesAsync(factory, stranger, ["secret.example.com"]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");

        using var adminByDomain = await GetJsonAsync(admin, "/api/v1/sites?search=secret");
        using var adminByOwner = await GetJsonAsync(admin, "/api/v1/sites?search=stranger");
        using var byDomain = await GetJsonAsync(customer, "/api/v1/sites?search=secret");
        using var byOwner = await GetJsonAsync(customer, "/api/v1/sites?search=stranger");
        using var mine = await GetJsonAsync(customer, "/api/v1/sites?search=own");

        Assert.Equal(1, adminByDomain.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, adminByOwner.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, byDomain.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, byDomain.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, byOwner.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["own.example.com"], Domains(mine));
    }

    /// <summary>
    /// The total counts every site, the last page holds the remainder, and a page past it is empty with
    /// the same total.
    /// </summary>
    [Fact]
    public async Task The_total_the_last_page_and_a_page_past_it_answer_as_the_contract_says()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        await SeedSitesAsync(
            factory,
            owner,
            [.. Enumerable.Range(0, 12).Select(index => { return $"site{index:D2}.example.com"; })],
            spread: true);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var last = await GetJsonAsync(admin, "/api/v1/sites?page=2&pageSize=10");
        using var beyond = await GetJsonAsync(admin, "/api/v1/sites?page=3&pageSize=10");

        Assert.Equal(12, last.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["site10.example.com", "site11.example.com"], Domains(last));
        Assert.Equal(0, beyond.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(12, beyond.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(3, beyond.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(10, beyond.RootElement.GetProperty("pageSize").GetInt32());
    }

    /// <summary>Sites sharing one creation instant page stably: the pages are disjoint and cover every site.</summary>
    [Fact]
    public async Task Sites_sharing_one_creation_instant_page_stably()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        await SeedSitesAsync(
            factory,
            owner,
            [.. Enumerable.Range(0, 15).Select(index => { return $"tie{index:D2}.example.com"; })]);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var first = await GetJsonAsync(admin, "/api/v1/sites?page=1&pageSize=10");
        using var second = await GetJsonAsync(admin, "/api/v1/sites?page=2&pageSize=10");
        using var again = await GetJsonAsync(admin, "/api/v1/sites?page=1&pageSize=10");

        var firstDomains = Domains(first);
        var secondDomains = Domains(second);
        Assert.Equal(firstDomains, Domains(again));
        Assert.Empty(firstDomains.Intersect(secondDomains));
        Assert.Equal(15, firstDomains.Concat(secondDomains).Distinct().Count());
    }

    /// <summary>
    /// A bound the list refuses answers 400 with its code: page 0, an unoffered size, an unknown sort, a
    /// bad filter.
    /// </summary>
    [Theory]
    [InlineData("page=0", "ListPageOutOfRange")]
    [InlineData("pageSize=101", "ListPageSizeNotOffered")]
    [InlineData("sort=documentRoot", "ListSortUnknown")]
    [InlineData("phpVersion=8.3.1", "PhpVersionInvalidFormat")]
    public async Task A_bound_the_list_refuses_answers_400_with_its_code(string queryString, string code)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory, "owner");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/sites?" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeOfAsync(response));
    }

    /// <summary>
    /// A value MVC cannot bind — a status number that names no member, a page or a page size that is
    /// not a number — is refused with 400 before the validator runs, and the answer carries the
    /// panel's code <c>RequestParameterInvalid</c> and a title naming the value, like every other
    /// validation 400. The validator's <c>SiteStatusUnknown</c> rule stays for callers that dispatch
    /// the query on the bus without HTTP binding.
    /// </summary>
    /// <param name="queryString">The unbindable query string.</param>
    /// <param name="parameter">The value the answer names.</param>
    [Theory]
    [InlineData("status=7", "Status")]
    [InlineData("page=abc", "Page")]
    [InlineData("pageSize=x", "PageSize")]
    public async Task A_value_that_cannot_bind_answers_400_with_a_code_naming_it(string queryString, string parameter)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory, "owner");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync("/api/v1/sites?" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("RequestParameterInvalid", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            $"The value given for “{parameter}” cannot be read.",
            body.RootElement.GetProperty("title").GetString());
    }

    /// <summary>
    /// The refusal of an unbindable value is localized like any other: Russian and Armenian callers
    /// read it in their own language, from the Sdk's resx table.
    /// </summary>
    /// <param name="language">The Accept-Language sent.</param>
    /// <param name="title">The title expected in that language.</param>
    [Theory]
    [InlineData("ru", "Значение параметра «Page» не удаётся прочитать.")]
    [InlineData("hy", "«Page» պարամետրի արժեքը հնարավոր չէ կարդալ։")]
    public async Task A_value_that_cannot_bind_is_refused_in_the_callers_language(string language, string title)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedWorldAsync(factory, "owner");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        admin.DefaultRequestHeaders.AcceptLanguage.Clear();
        admin.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));

        var response = await admin.GetAsync("/api/v1/sites?page=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("RequestParameterInvalid", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(title, body.RootElement.GetProperty("title").GetString());
    }

    /// <summary>
    /// The typed filters bind from the query string by name and keep only their rows. The enum
    /// filters are written in the camelCase the JSON answers with and the SPA sends
    /// (<c>disabled</c>, <c>php</c>, <c>reverseProxy</c>); a value that did not bind would be a 400,
    /// so the empty reverse-proxy page is a bound filter matching no row, not a refused one.
    /// </summary>
    [Fact]
    public async Task The_typed_filters_bind_from_the_query_string_and_keep_only_their_rows()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        await SeedSitesAsync(factory, owner, ["php83.example.com"]);
        using (var scope = factory.Services.CreateScope())
        {
            // Added, never queried: outside a request no principal is an administrator, so the
            // tenant filter would hide every row from a read here.
            var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
            var off = NewSite(owner, "off.example.com", "8.3");
            off.Disable();
            sites.Sites.AddRange(off, NewSite(owner, "php82.example.com", "8.2"));
            await sites.SaveChangesAsync();
        }

        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var disabled = await GetJsonAsync(admin, "/api/v1/sites?status=disabled");
        using var php82 = await GetJsonAsync(admin, "/api/v1/sites?phpVersion=8.2&backendType=php");
        using var proxied = await GetJsonAsync(admin, "/api/v1/sites?backendType=reverseProxy");

        Assert.Equal(["off.example.com"], Domains(disabled));
        Assert.Equal(["php82.example.com"], Domains(php82));
        Assert.Equal(0, proxied.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// Every sort key translates to an ORDER BY PostgreSQL runs — a string (domain), a timestamp
    /// (createdAt) and an enum (status) — and the descending order is the ascending one reversed.
    /// Each key is unique across the two rows, so the order is the key's and not the tie-breaker's.
    /// </summary>
    /// <param name="field">The sort key.</param>
    /// <param name="expected">The domains in ascending order of that key.</param>
    [Theory]
    [InlineData("domain", new[] { "a.example.com", "b.example.com" })]
    [InlineData("createdAt", new[] { "b.example.com", "a.example.com" })]
    [InlineData("status", new[] { "b.example.com", "a.example.com" })]
    public async Task Every_sort_key_orders_on_postgresql_in_both_directions(string field, string[] expected)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var owner = await SeedWorldAsync(factory, "owner");
        using (var scope = factory.Services.CreateScope())
        {
            // Status orders by its explicit rank, serving before suspended — not by the stored name,
            // which alphabetically would put Disabled first — so disabling a makes the order b, a.
            var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
            var first = NewSite(owner, "a.example.com", "8.3", Epoch.AddDays(1));
            first.Disable();
            sites.Sites.AddRange(first, NewSite(owner, "b.example.com", "8.3", Epoch));
            await sites.SaveChangesAsync();
        }

        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var ascending = await GetJsonAsync(admin, "/api/v1/sites?sort=" + field);
        using var descending = await GetJsonAsync(admin, "/api/v1/sites?sort=-" + field);

        Assert.Equal(expected, Domains(ascending));
        Assert.Equal(expected.Reverse(), Domains(descending));
    }

    /// <summary>Migrates, seeds the administrator, and stores one account, returning its id.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountName">The account to store.</param>
    private static async Task<Guid> SeedWorldAsync(WebApplicationFactory<Program> factory, string accountName)
    {
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, accountName);
        return accountId;
    }

    /// <summary>Stores one PHP site per domain for the account.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domains">The sites' primary domains.</param>
    /// <param name="spread">
    /// True to create each site a day after the one before, in the order given; false to give them all
    /// one creation instant.
    /// </param>
    private static async Task SeedSitesAsync(
        WebApplicationFactory<Program> factory,
        Guid accountId,
        string[] domains,
        bool spread = false)
    {
        // Written through a context resolved outside a request, so no tenant filter separates the
        // seed: the filter under test is the only thing that can.
        using var scope = factory.Services.CreateScope();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        for (var index = 0; index < domains.Length; index++)
        {
            sites.Sites.Add(NewSite(accountId, domains[index], "8.3", spread ? Epoch.AddDays(index) : Epoch));
        }

        await sites.SaveChangesAsync();
    }

    /// <summary>Builds one PHP-backed site row.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="phpVersion">The bound PHP version.</param>
    /// <param name="createdAt">The creation instant, <see cref="Epoch"/> when omitted.</param>
    private static Site NewSite(Guid accountId, string domain, string phpVersion, DateTimeOffset? createdAt = null)
    {
        return new Site(
            Guid.NewGuid(),
            accountId,
            domain,
            [],
            SiteBackendType.Php,
            phpVersion,
            string.Empty,
            $"/home/acct/sites/{domain}",
            createdAt ?? Epoch);
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

    /// <summary>The domains on a page, in order.</summary>
    /// <param name="page">The page's JSON.</param>
    private static List<string> Domains(JsonDocument page)
    {
        return page.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => { return item.GetProperty("domain").GetString()!; })
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
