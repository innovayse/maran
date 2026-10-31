using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The paged sessions list (<c>GET /api/v1/sessions</c>) over real HTTP against a real PostgreSQL:
/// the owner comes from the caller's token and from nothing a request can name, the search is an
/// <c>ILIKE</c> only PostgreSQL runs, and the paging bounds are refused before the handler is reached.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class SessionListEndpointTests : IAsyncLifetime
{
    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SessionListEndpointTests(PostgresFixture postgres)
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
    /// The IDOR case: a user sees only their own sessions and their own total, a search for another
    /// user's device finds nothing, and a <c>userId</c> in the query string is not bound — the owner
    /// is the token's, whatever the request names.
    /// </summary>
    /// <remarks>
    /// Bob's own answer to the same search is asserted first, so Alice's zero is the owner scope and
    /// not a search that matched nothing for anybody.
    /// </remarks>
    [Fact]
    public async Task A_user_sees_only_their_own_sessions_whatever_the_request_names()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedUsersAsync(factory, "alice", "bob");
        using var alice = await EndpointHarness.SignInAsync(factory, "alice");
        using var bob = await EndpointHarness.SignInAsync(factory, "bob");
        var bobId = await UserIdAsync(factory, "bob");
        await SeedSessionsAsync(factory, "bob", ("198.51.100.4", "BobPhone/1.0"));

        using var bobsOwn = await GetJsonAsync(bob, "/api/v1/sessions?search=BobPhone");
        using var bySearch = await GetJsonAsync(alice, "/api/v1/sessions?search=BobPhone");
        using var byUserId = await GetJsonAsync(alice, $"/api/v1/sessions?userId={bobId}");

        Assert.Equal(1, bobsOwn.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, bySearch.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(0, bySearch.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(1, byUserId.RootElement.GetProperty("total").GetInt32());
        var only = Assert.Single(byUserId.RootElement.GetProperty("items").EnumerateArray());
        Assert.True(only.GetProperty("isCurrent").GetBoolean());
    }

    /// <summary>
    /// The search runs over the address and the user agent, case-insensitively, and matches a LIKE
    /// wildcard literally rather than honouring it.
    /// </summary>
    [Fact]
    public async Task The_search_reaches_the_address_and_the_user_agent_and_matches_literally()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedUsersAsync(factory, "alice");
        using var alice = await EndpointHarness.SignInAsync(factory, "alice");
        await SeedSessionsAsync(
            factory,
            "alice",
            ("203.0.113.7", "Firefox/131"),
            ("198.51.100.4", "Safari_17"),
            ("192.0.2.1", "SafariX17"));

        using var byAddress = await GetJsonAsync(alice, "/api/v1/sessions?search=203.0.113");
        using var byAgent = await GetJsonAsync(alice, "/api/v1/sessions?search=FIREFOX");
        using var underscore = await GetJsonAsync(alice, "/api/v1/sessions?search=" + Uri.EscapeDataString("i_1"));

        Assert.Equal(["Firefox/131"], UserAgents(byAddress));
        Assert.Equal(["Firefox/131"], UserAgents(byAgent));
        Assert.Equal(["Safari_17"], UserAgents(underscore));
    }

    /// <summary>
    /// The total counts every live session, the last page holds the remainder, and a page past it is
    /// empty with the same total.
    /// </summary>
    [Fact]
    public async Task The_total_the_last_page_and_a_page_past_it_answer_as_the_contract_says()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedUsersAsync(factory, "alice");
        using var alice = await EndpointHarness.SignInAsync(factory, "alice");
        await SeedSessionsAsync(
            factory,
            "alice",
            [.. Enumerable.Range(0, 11).Select(index => { return ($"192.0.2.{index}", $"Device{index:D2}"); })]);

        using var last = await GetJsonAsync(alice, "/api/v1/sessions?page=2&pageSize=10");
        using var beyond = await GetJsonAsync(alice, "/api/v1/sessions?page=3&pageSize=10");

        Assert.Equal(12, last.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, last.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, beyond.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(12, beyond.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>Every sort key orders on PostgreSQL, and the descending order is the ascending one reversed.</summary>
    /// <param name="field">The sort key.</param>
    [Theory]
    [InlineData("issuedAt")]
    [InlineData("expiresAt")]
    public async Task Every_sort_key_orders_on_postgresql_in_both_directions(string field)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedUsersAsync(factory, "alice");
        using var alice = await EndpointHarness.SignInAsync(factory, "alice");
        await SeedSessionsAsync(factory, "alice", ("192.0.2.1", "Older"), ("192.0.2.2", "Oldest"));

        using var ascending = await GetJsonAsync(alice, "/api/v1/sessions?sort=" + field);
        using var descending = await GetJsonAsync(alice, "/api/v1/sessions?sort=-" + field);

        // Seeded sessions are issued a day apart, oldest last in the argument list, and the signed-in
        // one is the newest of all — so both keys put them in the same order.
        Assert.Equal(["Oldest", "Older"], UserAgents(ascending).Take(2));
        Assert.Equal(UserAgents(ascending).AsEnumerable().Reverse(), UserAgents(descending));
    }

    /// <summary>A bound the list refuses answers 400 with its code.</summary>
    /// <param name="queryString">The query string sent.</param>
    /// <param name="code">The machine code the refusal must carry.</param>
    [Theory]
    [InlineData("page=0", "ListPageOutOfRange")]
    [InlineData("pageSize=101", "ListPageSizeNotOffered")]
    [InlineData("sort=ipAddress", "ListSortUnknown")]
    [InlineData("sort=-", "ListSortUnknown")]
    public async Task A_bound_the_list_refuses_answers_400_with_its_code(string queryString, string code)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await SeedUsersAsync(factory, "alice");
        using var alice = await EndpointHarness.SignInAsync(factory, "alice");

        var response = await alice.GetAsync("/api/v1/sessions?" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>Migrates and creates one customer login per name, each owning an account of that name.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="usernames">The logins to create.</param>
    private static async Task SeedUsersAsync(WebApplicationFactory<Program> factory, params string[] usernames)
    {
        await EndpointHarness.MigrateAsync(factory);
        foreach (var username in usernames)
        {
            var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, username);
            await EndpointHarness.SeedCustomerAsync(factory, username, accountId);
        }
    }

    /// <summary>The id of the named login.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login name.</param>
    private static async Task<Guid> UserIdAsync(WebApplicationFactory<Program> factory, string username)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users
            .Where(user => user.Username == username)
            .Select(user => user.Id)
            .SingleAsync();
    }

    /// <summary>
    /// Stores one live session per device for the named login, each issued a day before the one listed
    /// before it, so they sort deterministically and all lie before the signed-in session.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login the sessions belong to.</param>
    /// <param name="devices">Each session's address and user agent.</param>
    private static async Task SeedSessionsAsync(
        WebApplicationFactory<Program> factory,
        string username,
        params (string Address, string UserAgent)[] devices)
    {
        var userId = await UserIdAsync(factory, username);
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        for (var index = 0; index < devices.Length; index++)
        {
            var issuedAt = now.AddDays(-(index + 1));
            identity.Sessions.Add(new Session(
                Guid.NewGuid(),
                userId,
                Guid.NewGuid(),
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                issuedAt,
                issuedAt.AddDays(14),
                devices[index].Address,
                devices[index].UserAgent));
        }

        await identity.SaveChangesAsync();
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

    /// <summary>The user agents on a page, in order.</summary>
    /// <param name="page">The page's JSON.</param>
    private static List<string> UserAgents(JsonDocument page)
    {
        return page.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => { return item.GetProperty("userAgent").GetString()!; })
            .ToList();
    }
}
