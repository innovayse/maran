using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The pending updates surface over real HTTP against real PostgreSQL (issue #324): what an
/// administrator reads, what a failed read answers, and that a customer reaches none of it.
/// </summary>
/// <remarks>
/// Who may reach the two routes is also asserted, route by route and in both directions, by
/// <see cref="MonitoringAuthorizationTests"/>, where both are listed in the gating fixture. This
/// class covers the shapes on the wire. The agent is the only substitution, and only because it is a
/// separate root process that runs the host's package manager.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class PendingUpdatesEndpointTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password both seeded users are given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PendingUpdatesEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>Resolves once this test's database exists.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>Resolves immediately; the shared server outlives the test.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>An administrator reads the page with every field, a missing installed version as null.</summary>
    [Fact]
    public async Task An_administrator_reads_the_page_with_every_field()
    {
        await using var factory = CreateFactory(Reporting(
            new AgentPendingUpdate("openssl", "3.0.13-2", "3.0.13-1", "noble-security"),
            new AgentPendingUpdate("bash", "5.2-2", null, string.Empty)));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("total").GetInt32());

        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("bash", items[0].GetProperty("packageName").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("installedVersion").ValueKind);
        Assert.Equal(string.Empty, items[0].GetProperty("source").GetString());
        Assert.Equal("openssl", items[1].GetProperty("packageName").GetString());
        Assert.Equal("3.0.13-2", items[1].GetProperty("availableVersion").GetString());
        Assert.Equal("3.0.13-1", items[1].GetProperty("installedVersion").GetString());
        Assert.Equal("noble-security", items[1].GetProperty("source").GetString());
    }

    /// <summary>A search, a sort and a page are applied to the agent's whole answer.</summary>
    [Fact]
    public async Task A_search_a_sort_and_a_page_are_applied_to_the_whole_answer()
    {
        await using var factory = CreateFactory(Reporting(
            new AgentPendingUpdate("libssl3", "2", "1", "updates"),
            new AgentPendingUpdate("openssl", "2", "1", "updates"),
            new AgentPendingUpdate("bash", "2", "1", "updates")));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates?search=SSL&sort=-packageName&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(
            ["openssl", "libssl3"],
            body.RootElement.GetProperty("items").EnumerateArray()
                .Select(row =>
                {
                    return row.GetProperty("packageName").GetString();
                })
                .ToArray());
    }

    /// <summary>A catalogue read successfully with nothing pending is an empty page, not an error.</summary>
    [Fact]
    public async Task A_catalogue_read_successfully_with_nothing_pending_is_an_empty_page()
    {
        await using var factory = CreateFactory(Reporting());
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>The count is the number of updates the agent reported.</summary>
    [Fact]
    public async Task The_count_is_the_number_of_updates_the_agent_reported()
    {
        await using var factory = CreateFactory(Reporting(
            new AgentPendingUpdate("bash", "2", "1", "updates"),
            new AgentPendingUpdate("curl", "2", "1", "updates")));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/monitoring/updates/count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("count").GetInt32());
    }

    /// <summary>A catalogue the agent could not read fails both routes and is never an empty page or a zero.</summary>
    /// <param name="path">The route read.</param>
    [Theory]
    [InlineData("/api/v1/monitoring/updates")]
    [InlineData("/api/v1/monitoring/updates/count")]
    public async Task A_catalogue_the_agent_could_not_read_fails_the_read_and_is_never_empty_or_zero(string path)
    {
        var agent = new StubAgentMonitorClient
        {
            PendingUpdates = Result<IReadOnlyList<AgentPendingUpdate>>.Fail(
                Error.Of("AgentSystemFailure", ErrorType.Failure)),
        };
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AgentSystemFailure", body.RootElement.GetProperty("code").GetString());
        Assert.False(body.RootElement.TryGetProperty("count", out _));
        Assert.False(body.RootElement.TryGetProperty("items", out _));
    }

    /// <summary>A request outside the paging contract is refused with its code.</summary>
    /// <param name="queryString">The offending query string.</param>
    /// <param name="code">The code the refusal must carry.</param>
    [Theory]
    [InlineData("sort=availableVersion", "ListSortUnknown")]
    [InlineData("pageSize=7", "ListPageSizeNotOffered")]
    [InlineData("page=0", "ListPageOutOfRange")]
    public async Task A_request_outside_the_paging_contract_is_refused_with_its_code(string queryString, string code)
    {
        await using var factory = CreateFactory(Reporting(new AgentPendingUpdate("bash", "2", "1", "updates")));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync($"/api/v1/monitoring/updates?{queryString}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>A signed-in customer is refused both routes and told nothing the agent reported.</summary>
    /// <param name="path">The route read.</param>
    [Theory]
    [InlineData("/api/v1/monitoring/updates")]
    [InlineData("/api/v1/monitoring/updates/count")]
    public async Task A_signed_in_customer_is_refused_and_told_nothing_the_agent_reported(string path)
    {
        await using var factory = CreateFactory(Reporting(new AgentPendingUpdate("bash", "2", "1", "updates")));
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("bash", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>An agent double reporting exactly the given updates.</summary>
    /// <param name="updates">What the agent reports as pending.</param>
    /// <returns>The double.</returns>
    private static StubAgentMonitorClient Reporting(params AgentPendingUpdate[] updates)
    {
        return new StubAgentMonitorClient
        {
            PendingUpdates = Result<IReadOnlyList<AgentPendingUpdate>>.Ok(updates),
        };
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent replaced.</summary>
    /// <param name="agent">The agent double this run answers from.</param>
    /// <returns>The booted host factory.</returns>
    private WebApplicationFactory<Program> CreateFactory(StubAgentMonitorClient agent)
    {
        return new ConfiguredHostFactory(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentMonitorClient>(agent);
            });
        });
    }

    /// <summary>Applies the migrations these tests need, the way the installer does before first boot.</summary>
    /// <remarks>Identity only, for the sign-in: the routes under test store nothing.</remarks>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds one administrator and one customer.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedUsersAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        identity.Users.Add(new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now));

        await identity.SaveChangesAsync();
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    /// <returns>A client whose requests carry the user's bearer token.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }
}
