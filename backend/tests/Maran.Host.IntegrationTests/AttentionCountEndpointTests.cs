using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Firewall.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Tasks.Persistence;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>GET /api/v1/dashboard/attention-count</c> over real HTTP: the one figure the navigation badge
/// shows, who may read it, and that a source which cannot be read leaves no figure at all.
/// </summary>
/// <remarks>
/// <para>
/// The figure is server-wide, not a tenant's row, so the isolation question is not "another account's
/// row answers 404" but "a customer is refused": the route is administrators-only by policy and the
/// sources are dispatched on a bus that carries no policy of its own.
/// </para>
/// <para>
/// <b>Every refusing assertion has an inverse control.</b> The administrator path is asserted to carry
/// a loaded figure in the same file as the customer path is asserted not to, so an endpoint refusing
/// everybody would not pass. The route is a read: it writes nothing, so there is no audit entry to
/// assert.
/// </para>
/// <para>
/// UNOBSERVED HERE: the count of failed tasks, expiring certificates, failed backups and bans is
/// asserted as a sum in <c>AttentionTallyTests</c> and not re-seeded here; this file observes the
/// route, the policy, the clock source and the unavailable state.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AttentionCountEndpointTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AttentionCountEndpointTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused outright.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        await using var factory = CreateFactory(new StubAgentMonitorClient());
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/dashboard/attention-count");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed-in customer is refused with 403 and is told nothing about the server.</summary>
    [Fact]
    public async Task A_signed_in_customer_is_refused()
    {
        await using var factory = CreateFactory(new StubAgentMonitorClient());
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/dashboard/attention-count");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// An administrator on a host where every source was read and none asks for attention is given a
    /// loaded zero: the all-clear, which is a real answer and not an unavailable figure.
    /// </summary>
    [Fact]
    public async Task An_administrator_on_a_quiet_host_is_given_a_loaded_zero()
    {
        await using var factory = CreateFactory(new StubAgentMonitorClient());
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/dashboard/attention-count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertLoaded(await ReadAsync(response), 0);
    }

    /// <summary>
    /// A clock that is not synchronised is counted: the figure is the dashboard's attention block
    /// added up, not a separate reading that could disagree with it.
    /// </summary>
    [Fact]
    public async Task A_clock_that_is_not_synchronised_is_counted_as_it_is_on_the_dashboard()
    {
        var agent = new StubAgentMonitorClient
        {
            ClockSync = Result<AgentClockSyncState>.Ok(AgentClockSyncState.NotSynchronized),
        };
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var count = await ReadAsync(await client.GetAsync("/api/v1/dashboard/attention-count"));
        var dashboard = await client.GetAsync("/api/v1/dashboard");
        using var body = JsonDocument.Parse(await dashboard.Content.ReadAsStringAsync());
        var clock = body.RootElement.GetProperty("attention").GetProperty("clock").GetProperty("data");

        AssertLoaded(count, 1);
        Assert.Equal("notSynchronized", clock.GetProperty("state").GetString());
    }

    /// <summary>
    /// A source that cannot answer leaves the figure unavailable rather than smaller. The Backups
    /// schema is not created, so that module's count fails; the clock is read and synchronised, so
    /// the only reason for the answer is the unread source.
    /// </summary>
    [Fact]
    public async Task A_source_that_cannot_answer_leaves_the_figure_unavailable()
    {
        await using var factory = CreateFactory(new StubAgentMonitorClient());
        await MigrateAsync(factory, leftOut: typeof(BackupsDbContext));
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/dashboard/attention-count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var section = await ReadAsync(response);
        Assert.Equal("unavailable", section.GetProperty("state").GetString());
        Assert.False(section.TryGetProperty("data", out _));
    }

    /// <summary>Boots the panel against this test's own database with the given monitoring agent.</summary>
    /// <param name="agent">The agent double the clock is read from.</param>
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

    /// <summary>Applies the migrations of the contexts the attention sources read through.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="leftOut">A context whose schema is NOT created, or <c>null</c> for none.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory, Type? leftOut = null)
    {
        Type[] contexts =
        [
            typeof(IdentityDbContext),
            typeof(AccountsDbContext),
            typeof(BackupsDbContext),
            typeof(SslDbContext),
            typeof(FirewallDbContext),
            typeof(TasksDbContext),
        ];

        using var scope = factory.Services.CreateScope();
        foreach (var context in contexts.Where(context =>
        {
            return context != leftOut;
        }))
        {
            var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(context);
            await dbContext.Database.MigrateAsync();
        }
    }

    /// <summary>Seeds one account with an administrator and a customer signed into it.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5, 3));
        var own = new Account(Guid.NewGuid(), "own", "own.example.com", planId, now);
        accounts.Accounts.Add(own);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        customer.AssignAccount(own.Id);
        identity.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), customer.Id, own.Id, now));
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();
    }

    /// <summary>Signs a seeded user in and returns a client carrying their token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">Which seeded user to sign in.</param>
    /// <returns>An authenticated client.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Reads an answer's body as a detached JSON element.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The body's root element.</returns>
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    /// <summary>Asserts a section was read and holds <paramref name="expected"/>.</summary>
    /// <param name="section">The section as the endpoint answered it.</param>
    /// <param name="expected">The number expected.</param>
    private static void AssertLoaded(JsonElement section, int expected)
    {
        Assert.Equal("loaded", section.GetProperty("state").GetString());
        Assert.Equal(expected, section.GetProperty("data").GetInt32());
    }
}
