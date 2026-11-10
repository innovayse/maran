using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The tenant boundary of the global search (<c>POST /api/v1/search</c>, issue #198) over real HTTP
/// against a real PostgreSQL: customer A cannot find customer B's site, even by its exact hostname, and
/// can find their own; a customer is offered no account; a reseller finds only the accounts it owns; an
/// API key is refused; the term is matched literally. These are the cases the module unit tests cannot
/// hold, because the match is an <c>ILIKE</c> only PostgreSQL evaluates.
/// </summary>
/// <remarks>
/// <para>
/// <b>How an empty answer is made able to fail.</b> Every "finds nothing" assertion is paired, in the same
/// test, with a control that finds the row through the same term: the administrator (whose scope is the
/// whole server) for a foreign row, the owner for their own. A search that matched nothing for anybody, or
/// a group that was never asked, would otherwise pass every one of them.
/// </para>
/// <para>
/// UNOBSERVED HERE: that the term never reaches a log. The integration host writes through Serilog's
/// console sink and offers no capture point without a production change, so that property is held by the
/// fan-out unit test (<c>SearchFanOutTests</c>), which plants a term and reads the recorded entries, and by
/// the endpoint taking the term from the body rather than the URL. A provider that exceeds its time budget
/// is likewise held there, not here.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SearchTenantIsolationEndpointTests : IAsyncLifetime
{
    /// <summary>The creation instant every seeded site shares.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SearchTenantIsolationEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Customer A cannot find customer B's site by its exact hostname, by a part of it, or by an alias,
    /// and is answered the same empty group a name that exists nowhere gets; customer A finds their own
    /// site by its domain and by its alias. The administrator finds B's site through the same terms, so
    /// the emptiness is the tenant scope and not a search that matched nothing.
    /// </summary>
    [Fact]
    public async Task A_customer_finds_their_own_site_but_not_another_customers_even_by_its_exact_hostname()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var (accountA, accountB) = await SeedTwoCustomersAsync(factory);
        await SeedSiteAsync(factory, accountA, "alpha-shop.example.com", ["www.alpha-shop.example.com"]);
        await SeedSiteAsync(factory, accountB, "bravo-secret.example.com", ["www.bravo-secret.example.com"]);
        using var customerA = await EndpointHarness.SignInAsync(factory, "customer-a");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        foreach (var foreign in new[] { "bravo-secret.example.com", "bravo-secret", "www.bravo-secret.example.com" })
        {
            using var asAdmin = await SearchAsync(admin, foreign);
            using var asCustomer = await SearchAsync(customerA, foreign);

            Assert.Equal(["bravo-secret.example.com"], TitlesOf(asAdmin, "site"));
            Assert.Empty(HitsOf(asCustomer, "site"));
            Assert.Equal("ready", StateOf(asCustomer, "site"));
        }

        using var missing = await SearchAsync(customerA, "nowhere-at-all.example.com");
        using var foreignExact = await SearchAsync(customerA, "bravo-secret.example.com");
        Assert.Equal(missing.RootElement.GetRawText(), foreignExact.RootElement.GetRawText());

        using var byDomain = await SearchAsync(customerA, "alpha-shop.example.com");
        using var byAlias = await SearchAsync(customerA, "www.alpha-shop");
        Assert.Equal(["alpha-shop.example.com"], TitlesOf(byDomain, "site"));
        Assert.Equal(["alpha-shop.example.com"], TitlesOf(byAlias, "site"));
        var aliasHit = HitsOf(byAlias, "site").Single();
        Assert.Equal("www.alpha-shop.example.com", aliasHit.GetProperty("subtitle").GetString());
        var ownHit = HitsOf(byDomain, "site").Single();
        Assert.Equal(accountA, await OwnerOfAsync(factory, ownHit.GetProperty("id").GetGuid()));
    }

    /// <summary>
    /// The same boundary read from the other side: customer B finds B's site and not A's, so neither
    /// customer's access is the other's by an ordering accident.
    /// </summary>
    [Fact]
    public async Task The_boundary_holds_in_both_directions_between_two_customers()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var (accountA, accountB) = await SeedTwoCustomersAsync(factory);
        await SeedSiteAsync(factory, accountA, "alpha-shop.example.com", []);
        await SeedSiteAsync(factory, accountB, "bravo-secret.example.com", []);
        using var customerB = await EndpointHarness.SignInAsync(factory, "customer-b");

        using var own = await SearchAsync(customerB, "bravo-secret");
        using var foreign = await SearchAsync(customerB, "alpha-shop.example.com");

        Assert.Equal(["bravo-secret.example.com"], TitlesOf(own, "site"));
        Assert.Empty(HitsOf(foreign, "site"));
    }

    /// <summary>
    /// A customer is offered no account — not another customer's by its exact name, and not their own —
    /// while the administrator finds both through the same terms.
    /// </summary>
    [Fact]
    public async Task A_customer_gets_no_accounts_group_hits_even_for_their_own_account()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedTwoCustomersAsync(factory);
        using var customerA = await EndpointHarness.SignInAsync(factory, "customer-a");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        foreach (var term in new[] { "acct-b", "acct-a" })
        {
            using var asAdmin = await SearchAsync(admin, term);
            using var asCustomer = await SearchAsync(customerA, term);

            Assert.Single(HitsOf(asAdmin, "account"));
            Assert.Empty(HitsOf(asCustomer, "account"));
        }
    }

    /// <summary>
    /// A customer whose plan does not sell the Sites feature finds none of their own sites, while a
    /// customer of the same shape whose plan does sell it finds theirs through the same term.
    /// </summary>
    [Fact]
    public async Task A_customer_whose_plan_lacks_sites_finds_no_site_of_their_own()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await MigrateSitesAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var noSites = Plan.Define(
            Guid.NewGuid(), "NoSites", 5_120, 5, 2, 3, 0, 5, 5, AccountFeature.All & ~AccountFeature.Sites);
        var (_, gated) = await EndpointHarness.SeedAccountAsync(factory, "acct-gated", noSites);
        var (_, open) = await EndpointHarness.SeedAccountAsync(factory, "acct-open");
        await EndpointHarness.SeedCustomerAsync(factory, "customer-gated", gated);
        await EndpointHarness.SeedCustomerAsync(factory, "customer-open", open);
        await SeedSiteAsync(factory, gated, "gated-site.example.com", []);
        await SeedSiteAsync(factory, open, "open-site.example.com", []);
        using var customerGated = await EndpointHarness.SignInAsync(factory, "customer-gated");
        using var customerOpen = await EndpointHarness.SignInAsync(factory, "customer-open");

        using var refused = await SearchAsync(customerGated, "gated-site");
        using var allowed = await SearchAsync(customerOpen, "open-site");

        Assert.Empty(HitsOf(refused, "site"));
        Assert.Equal(["open-site.example.com"], TitlesOf(allowed, "site"));
    }

    /// <summary>
    /// A reseller finds the account it owns and not another reseller's or the administrator's house
    /// account, by name, by a part of it or by owner e-mail; it is offered no site; and each reseller
    /// finds only its own in both directions. The administrator finds all of them through the same terms.
    /// </summary>
    /// <param name="actor">The reseller that searches.</param>
    [Theory]
    [InlineData("alpha")]
    [InlineData("bravo")]
    public async Task A_reseller_finds_only_the_accounts_it_owns(string actor)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).WithEveryModuleSchema().BuildAsync();
        var self = world.Reseller(actor);
        var stranger = world.Reseller(actor == "alpha" ? "bravo" : "alpha");
        using var client = await world.SignInAsync(actor);
        using var admin = await world.SignInAsync("admin");

        using var everything = await SearchAsync(client, "one");
        using var strangerByName = await SearchAsync(client, stranger.AccountName);
        using var strangerByEmail = await SearchAsync(client, $"{stranger.AccountName}@example.com");
        using var house = await SearchAsync(client, ResellerWorld.HouseAccountName);
        using var strangerAsAdmin = await SearchAsync(admin, stranger.AccountName);
        using var houseAsAdmin = await SearchAsync(admin, ResellerWorld.HouseAccountName);

        Assert.Equal([self.AccountName], TitlesOf(everything, "account"));
        Assert.Equal(self.AccountId, HitsOf(everything, "account").Single().GetProperty("id").GetGuid());
        Assert.Empty(HitsOf(strangerByName, "account"));
        Assert.Empty(HitsOf(strangerByEmail, "account"));
        Assert.Empty(HitsOf(house, "account"));
        Assert.Empty(HitsOf(everything, "site"));
        Assert.Equal([stranger.AccountName], TitlesOf(strangerAsAdmin, "account"));
        Assert.Equal([ResellerWorld.HouseAccountName], TitlesOf(houseAsAdmin, "account"));
    }

    /// <summary>
    /// An API key is not a signed-in person: the search refuses it with 401 whatever its scopes, while the
    /// administrator's token on the same host is answered and finds the account.
    /// </summary>
    [Fact]
    public async Task An_api_key_gets_nothing()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        await MigrateSitesAsync(host.Factory);
        using var admin = await host.SignInAdministratorAsync();
        var issued = await ApiKeyHost.CreateKeyAsync(
            admin, "search probe", [ApiKeyScopes.SelfRead, ApiKeyScopes.ServerRead]);
        using var keyClient = host.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/search")
        {
            Content = JsonContent.Create(new { term = ApiKeyHost.AccountName }),
        };
        request.Headers.Add("X-Forwarded-For", ApiKeyHost.DefaultAddress);
        request.Headers.Add("X-Api-Key", issued.Secret);

        using var refused = await keyClient.SendAsync(request);
        using var control = await SearchAsync(admin, ApiKeyHost.AccountName);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.DoesNotContain(ApiKeyHost.AccountName, await refused.Content.ReadAsStringAsync());
        Assert.Equal([ApiKeyHost.AccountName], TitlesOf(control, "account"));
    }

    /// <summary>
    /// A request with no credentials is refused with 401, and a term shorter than two characters is refused
    /// with 400 and its code by the server, not only by the palette.
    /// </summary>
    [Fact]
    public async Task An_anonymous_request_is_refused_and_a_one_character_term_is_a_400()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedTwoCustomersAsync(factory);
        using var anonymous = factory.CreateClient();
        using var customerA = await EndpointHarness.SignInAsync(factory, "customer-a");

        using var unauthenticated = await anonymous.PostAsJsonAsync("/api/v1/search", new { term = "acct" });
        using var tooShort = await customerA.PostAsJsonAsync("/api/v1/search", new { term = "a" });

        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal("SearchTermTooShort", await ProblemDocument.CodeAsync(tooShort));
    }

    /// <summary>
    /// The term is matched literally: <c>%</c> and <c>_</c> are not wildcards, so a term made of them
    /// matches nothing, while an underscore matches only the hostname that holds one. The positive match
    /// shows the search ran and that the wildcard was escaped rather than honoured.
    /// </summary>
    [Fact]
    public async Task The_term_is_matched_literally_and_wildcards_do_not_widen_it()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        var (accountA, _) = await SeedTwoCustomersAsync(factory);
        await SeedSiteAsync(factory, accountA, "ab.example.com", []);
        await SeedSiteAsync(factory, accountA, "a_b.example.com", []);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var percents = await SearchAsync(admin, "%%");
        using var underscores = await SearchAsync(admin, "__");
        using var hostile = await SearchAsync(admin, "%_\\'; drop");
        using var literal = await SearchAsync(admin, "a_");
        using var anyCase = await SearchAsync(admin, "A_B.EXAMPLE");

        Assert.Empty(HitsOf(percents, "site"));
        Assert.Empty(HitsOf(percents, "account"));
        Assert.Empty(HitsOf(underscores, "site"));
        Assert.Empty(HitsOf(hostile, "site"));
        Assert.Equal(["a_b.example.com"], TitlesOf(literal, "site"));
        Assert.Equal(["a_b.example.com"], TitlesOf(anyCase, "site"));
    }

    /// <summary>
    /// Migrates every schema the search reads, seeds the administrator, and stores two accounts, each with
    /// its own customer login.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The two account ids, <c>acct-a</c> then <c>acct-b</c>.</returns>
    private static async Task<(Guid AccountA, Guid AccountB)> SeedTwoCustomersAsync(
        WebApplicationFactory<Program> factory)
    {
        await EndpointHarness.MigrateAsync(factory);
        await MigrateSitesAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountA) = await EndpointHarness.SeedAccountAsync(factory, "acct-a");
        var (_, accountB) = await EndpointHarness.SeedAccountAsync(factory, "acct-b");
        await EndpointHarness.SeedCustomerAsync(factory, "customer-a", accountA);
        await EndpointHarness.SeedCustomerAsync(factory, "customer-b", accountB);
        return (accountA, accountB);
    }

    /// <summary>Creates the Sites module's schema.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateSitesAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
    }

    /// <summary>Stores one PHP site, with its aliases, for the account.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="aliases">The hostnames the site also answers for.</param>
    private static async Task SeedSiteAsync(
        WebApplicationFactory<Program> factory,
        Guid accountId,
        string domain,
        IReadOnlyList<string> aliases)
    {
        // Written through a context resolved outside a request, so no tenant filter separates the seed:
        // the filter under test is the only thing that can.
        using var scope = factory.Services.CreateScope();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        sites.Sites.Add(new Site(
            Guid.NewGuid(),
            accountId,
            domain,
            aliases,
            SiteBackendType.Php,
            "8.3",
            string.Empty,
            string.Empty,
            0,
            string.Empty,
            $"/home/acct/sites/{domain}",
            Epoch));
        await sites.SaveChangesAsync();
    }

    /// <summary>Reads which account owns a site, bypassing the tenant filter the way a seed does.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="siteId">The site.</param>
    /// <returns>The owning account's id.</returns>
    private static async Task<Guid> OwnerOfAsync(WebApplicationFactory<Program> factory, Guid siteId)
    {
        using var scope = factory.Services.CreateScope();
        var sites = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        return await sites.Sites.IgnoreQueryFilters().AsNoTracking()
            .Where(site => site.Id == siteId)
            .Select(site => site.AccountId)
            .SingleAsync();
    }

    /// <summary>Posts a search and reads the successful answer.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="term">The term to search for.</param>
    /// <returns>The parsed answer, which the caller disposes.</returns>
    private static async Task<JsonDocument> SearchAsync(HttpClient client, string term)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/search", new { term });
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"The search answered {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text);
    }

    /// <summary>Finds a result group by its kind, ignoring the case the JSON spells it in.</summary>
    /// <param name="answer">The search answer.</param>
    /// <param name="kind">The group's kind.</param>
    /// <returns>The group element; the assertion fails when the module was never asked.</returns>
    private static JsonElement GroupOf(JsonDocument answer, string kind)
    {
        var groups = answer.RootElement.GetProperty("groups").EnumerateArray().ToList();
        var matching = groups.Where(group =>
        {
            return string.Equals(group.GetProperty("kind").GetString(), kind, StringComparison.OrdinalIgnoreCase);
        }).ToList();
        Assert.True(
            matching.Count == 1,
            $"Expected one '{kind}' group, found {matching.Count} in {answer.RootElement.GetRawText()}");
        return matching[0];
    }

    /// <summary>The hits of one group.</summary>
    /// <param name="answer">The search answer.</param>
    /// <param name="kind">The group's kind.</param>
    /// <returns>The hit elements.</returns>
    private static List<JsonElement> HitsOf(JsonDocument answer, string kind)
    {
        return [.. GroupOf(answer, kind).GetProperty("hits").EnumerateArray()];
    }

    /// <summary>The titles of one group's hits, in answer order.</summary>
    /// <param name="answer">The search answer.</param>
    /// <param name="kind">The group's kind.</param>
    /// <returns>The titles.</returns>
    private static List<string> TitlesOf(JsonDocument answer, string kind)
    {
        return [.. HitsOf(answer, kind).Select(hit => { return hit.GetProperty("title").GetString()!; })];
    }

    /// <summary>The state a group reports.</summary>
    /// <param name="answer">The search answer.</param>
    /// <param name="kind">The group's kind.</param>
    /// <returns>The state, lower-cased.</returns>
    private static string StateOf(JsonDocument answer, string kind)
    {
        return GroupOf(answer, kind).GetProperty("state").GetString()!.ToLowerInvariant();
    }
}
