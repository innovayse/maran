using System.Net;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Firewall.Domain.Entities;
using Maran.Modules.Firewall.Domain.Enums;
using Maran.Modules.Firewall.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The two paged firewall lists — the bans in force (<c>GET /api/v1/firewall/bans</c>) and the
/// whitelist (<c>GET /api/v1/firewall/whitelist</c>) — over real HTTP against a real PostgreSQL: the
/// search is an <c>ILIKE</c> only PostgreSQL runs, the in-force rule is applied before the count, and
/// the paging bounds are refused before the handler is reached. Who may reach either list at all is
/// <see cref="FirewallAuthorizationTests"/>.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class FirewallListEndpointTests : IAsyncLifetime
{
    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public FirewallListEndpointTests(PostgresFixture postgres)
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
    /// A lifted ban, an expired one and one with less than a second left are neither listed nor
    /// counted: the total is the number of bans in force, the same figure the page is cut from.
    /// </summary>
    [Fact]
    public async Task Only_bans_in_force_are_listed_and_counted()
    {
        await using var factory = CreateFactory();
        await SeedWorldAsync(factory);
        await SeedBansAsync(factory, now =>
        {
            var lifted = Ban("203.0.113.5", now.AddDays(-5), now.AddHours(1));
            lifted.Lift(now);
            return
            [
                Ban("203.0.113.1", now.AddDays(-1), now.AddHours(1)),
                Ban("203.0.113.2", now.AddDays(-2), null),
                Ban("203.0.113.3", now.AddDays(-3), now.AddHours(-1)),
                Ban("203.0.113.4", now.AddDays(-4), now.AddMilliseconds(200)),
                lifted,
            ];
        });
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var page = await GetJsonAsync(admin, "/api/v1/firewall/bans");

        Assert.Equal(2, page.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["203.0.113.1", "203.0.113.2"], Values(page, "ipAddress"));
    }

    /// <summary>
    /// The ban search matches inside the address, and a LIKE wildcard matches literally: <c>0_1</c>
    /// finds nothing, where an unescaped underscore would match every <c>0.1</c>.
    /// </summary>
    [Fact]
    public async Task The_ban_search_matches_inside_the_address_and_literally()
    {
        await using var factory = CreateFactory();
        await SeedWorldAsync(factory);
        await SeedBansAsync(factory, now =>
        {
            return
            [
                .. Enumerable.Range(0, 3).Select(index =>
                {
                    return Ban($"198.51.100.{index + 10}", now.AddMinutes(-index), now.AddHours(1));
                }),
            ];
        });
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var byPart = await GetJsonAsync(admin, "/api/v1/firewall/bans?search=100.11");
        using var wildcard = await GetJsonAsync(admin, "/api/v1/firewall/bans?search=" + Uri.EscapeDataString("0_1"));

        Assert.Equal(["198.51.100.11"], Values(byPart, "ipAddress"));
        Assert.Equal(0, wildcard.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// The ban total counts every ban in force, the last page holds the remainder, and a page past it
    /// is empty with the same total.
    /// </summary>
    [Fact]
    public async Task The_ban_total_the_last_page_and_a_page_past_it_answer_as_the_contract_says()
    {
        await using var factory = CreateFactory();
        await SeedWorldAsync(factory);
        await SeedBansAsync(factory, now =>
        {
            return
            [
                .. Enumerable.Range(0, 12).Select(index =>
                {
                    return Ban($"192.0.2.{index + 1}", now.AddMinutes(-index), now.AddHours(1));
                }),
            ];
        });
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var last = await GetJsonAsync(admin, "/api/v1/firewall/bans?page=2&pageSize=10");
        using var beyond = await GetJsonAsync(admin, "/api/v1/firewall/bans?page=3&pageSize=10");

        Assert.Equal(12, last.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(["192.0.2.11", "192.0.2.12"], Values(last, "ipAddress"));
        Assert.Equal(0, beyond.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(12, beyond.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// Every ban sort key orders on PostgreSQL, and descending is ascending reversed. The placing time and
    /// the expiry put the two rows in opposite orders, so neither answer can be the tie-breaker's.
    /// </summary>
    /// <param name="field">The sort key.</param>
    /// <param name="expected">The addresses in ascending order of that key.</param>
    [Theory]
    [InlineData("address", new[] { "192.0.2.1", "192.0.2.2" })]
    [InlineData("bannedAt", new[] { "192.0.2.1", "192.0.2.2" })]
    [InlineData("expiresAt", new[] { "192.0.2.2", "192.0.2.1" })]
    public async Task Every_ban_sort_key_orders_on_postgresql_in_both_directions(string field, string[] expected)
    {
        await using var factory = CreateFactory();
        await SeedWorldAsync(factory);
        await SeedBansAsync(factory, now =>
        {
            return
            [
                Ban("192.0.2.1", now.AddHours(-2), now.AddHours(5)),
                Ban("192.0.2.2", now.AddHours(-1), now.AddHours(3)),
            ];
        });
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var ascending = await GetJsonAsync(admin, "/api/v1/firewall/bans?sort=" + field);
        using var descending = await GetJsonAsync(admin, "/api/v1/firewall/bans?sort=-" + field);

        Assert.Equal(expected, Values(ascending, "ipAddress"));
        Assert.Equal(expected.Reverse(), Values(descending, "ipAddress"));
    }

    /// <summary>
    /// The whitelist search reaches the range and the note, and the list orders by either key on
    /// PostgreSQL.
    /// </summary>
    [Fact]
    public async Task The_whitelist_search_reaches_the_range_and_the_note_and_both_keys_order()
    {
        await using var factory = CreateFactory();
        await SeedWorldAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var firewall = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
            var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
            firewall.WhitelistEntries.AddRange(
                new WhitelistEntry(Guid.NewGuid(), "203.0.113.0/24", "Office", now.AddDays(-2)),
                new WhitelistEntry(Guid.NewGuid(), "198.51.100.7/32", "monitoring", now.AddDays(-1)));
            await firewall.SaveChangesAsync();
        }

        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        using var byNote = await GetJsonAsync(admin, "/api/v1/firewall/whitelist?search=OFFICE");
        using var byRange = await GetJsonAsync(admin, "/api/v1/firewall/whitelist?search=100.7");
        using var oldestFirst = await GetJsonAsync(admin, "/api/v1/firewall/whitelist");
        using var byCidr = await GetJsonAsync(admin, "/api/v1/firewall/whitelist?sort=cidr");

        Assert.Equal(["203.0.113.0/24"], Values(byNote, "cidr"));
        Assert.Equal(["198.51.100.7/32"], Values(byRange, "cidr"));
        Assert.Equal(["203.0.113.0/24", "198.51.100.7/32"], Values(oldestFirst, "cidr"));
        Assert.Equal(["198.51.100.7/32", "203.0.113.0/24"], Values(byCidr, "cidr"));
    }

    /// <summary>A bound either list refuses answers 400 with its code.</summary>
    /// <param name="path">The list and the query string sent.</param>
    /// <param name="code">The machine code the refusal must carry.</param>
    [Theory]
    [InlineData("/api/v1/firewall/bans?page=0", "ListPageOutOfRange")]
    [InlineData("/api/v1/firewall/bans?pageSize=101", "ListPageSizeNotOffered")]
    [InlineData("/api/v1/firewall/bans?sort=reason", "ListSortUnknown")]
    [InlineData("/api/v1/firewall/whitelist?pageSize=7", "ListPageSizeNotOffered")]
    [InlineData("/api/v1/firewall/whitelist?sort=note", "ListSortUnknown")]
    public async Task A_bound_either_list_refuses_answers_400_with_its_code(string path, string code)
    {
        await using var factory = CreateFactory();
        await SeedWorldAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>Boots the host with the agent replaced: it is a root process that cannot be present here.</summary>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return EndpointHarness.CreateFactory(_pg, services =>
        {
            services.AddSingleton<IAgentFirewallClient>(new StubAgentFirewallClient());
        });
    }

    /// <summary>Migrates what the lists need and seeds the administrator.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedWorldAsync(WebApplicationFactory<Program> factory)
    {
        await EndpointHarness.MigrateAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<FirewallDbContext>().Database.MigrateAsync();
        }

        await EndpointHarness.SeedAdministratorAsync(factory);
    }

    /// <summary>Stores the ban episodes <paramref name="build"/> makes against the host's own clock.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="build">Builds the episodes from the current instant.</param>
    private static async Task SeedBansAsync(
        WebApplicationFactory<Program> factory,
        Func<DateTimeOffset, BanEpisode[]> build)
    {
        using var scope = factory.Services.CreateScope();
        var firewall = scope.ServiceProvider.GetRequiredService<FirewallDbContext>();
        firewall.BanEpisodes.AddRange(build(scope.ServiceProvider.GetRequiredService<IClock>().UtcNow));
        await firewall.SaveChangesAsync();
    }

    /// <summary>Builds a manual ban episode.</summary>
    /// <param name="address">The banned address.</param>
    /// <param name="bannedAt">When it was placed.</param>
    /// <param name="expiresAt">When it runs out, or null for one that lasts until lifted.</param>
    private static BanEpisode Ban(string address, DateTimeOffset bannedAt, DateTimeOffset? expiresAt)
    {
        return new BanEpisode(Guid.NewGuid(), address, BanReason.Manual, null, 0, bannedAt, expiresAt);
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

    /// <summary>One text property of every row on a page, in order.</summary>
    /// <param name="page">The page's JSON.</param>
    /// <param name="property">The camel-cased property to read.</param>
    private static List<string> Values(JsonDocument page, string property)
    {
        return page.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => { return item.GetProperty(property).GetString()!; })
            .ToList();
    }
}
