using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>GET /api/v1/dashboard</c> over real HTTP: who may read what this server holds, and what a
/// caller who may not read it receives instead.
/// </summary>
/// <remarks>
/// <para>
/// This endpoint composes its answer from queries whose own HTTP surfaces are administrators-only,
/// dispatched on the bus where those surfaces' policies do not apply. Its authorization is therefore
/// its own, and these are the tests that hold it to that.
/// </para>
/// <para>
/// <b>Every refusing assertion has an inverse control.</b> A payload emptied for everybody —
/// administrators included — would pass a test that only hands it a customer, leaving the whole
/// screen dead while looking secure; so the administrator path is asserted to carry the seeded
/// account in the same breath as the customer path is asserted not to.
/// </para>
/// <para>
/// UNOBSERVED HERE: these tests see the RESPONSE, so they cannot tell a section that was never read
/// from one that was read and discarded. The endpoint checks <c>IsAdmin</c> before dispatching
/// anything, and the gap between "filtered on the way out" and "never read" is invisible from
/// outside — a reviewer has to read <c>DashboardEndpoint.DescribeAsync</c> for that. It would be
/// observable by recording dispatches on a wrapped <c>IMessageBus</c>, which is not done here.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class DashboardEndpointTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public DashboardEndpointTests(PostgresFixture postgres)
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
    /// <remarks>
    /// 401 and not an empty payload. <c>/api/v1/modules</c> answers anonymously because it describes
    /// what this build composed; this route describes what the server HOLDS, and the count of
    /// accounts on a machine is not a fact to hand to whoever asks.
    /// </remarks>
    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/dashboard");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A signed-in customer is answered 200, told they are not an administrator, and given no
    /// section of the server's state.
    /// </summary>
    /// <remarks>
    /// 200 and deliberately not 403: this is the panel's landing route for everyone, and the health
    /// verdict it also carries must still reach a customer. The refusal is in the CONTENTS, and it
    /// is asserted as JSON null per section rather than as an absent property — the SPA relies on
    /// null meaning "not for you", and a missing property would be a different contract.
    /// </remarks>
    [Fact]
    public async Task A_signed_in_customer_is_given_no_part_of_the_servers_state()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.False(root.GetProperty("isAdministrator").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("resources").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("counts").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("attention").ValueKind);
        Assert.Empty(root.GetProperty("services").EnumerateArray());
        Assert.Empty(root.GetProperty("recentAudit").EnumerateArray());
    }

    /// <summary>
    /// An administrator is told so and is given the counts, with the seeded account counted.
    /// </summary>
    /// <remarks>
    /// The inverse control the customer test needs. The account count is asserted as the exact
    /// number seeded rather than merely "more than zero": a count wired to the wrong listing, or to
    /// a listing that answered empty, would pass a "not null" check while reporting a server that
    /// holds nothing.
    /// </remarks>
    [Fact]
    public async Task An_administrator_is_given_the_counts_and_the_seeded_account_is_in_them()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.True(root.GetProperty("isAdministrator").GetBoolean());

        var counts = root.GetProperty("counts");
        Assert.Equal(JsonValueKind.Object, counts.ValueKind);
        Assert.Equal(1, counts.GetProperty("accounts").GetInt32());

        // Present and zero, which is the ordinary answer on a server with one account and nothing on
        // it — and a different answer from "withheld". A dashboard that showed an empty state here
        // would read as a failure on a freshly installed panel.
        Assert.Equal(0, counts.GetProperty("sites").GetInt32());
        Assert.Equal(0, counts.GetProperty("databases").GetInt32());

        // The attention section is an object of zeros rather than null: nothing needs attention on
        // this host, which the panel knows, as against not being allowed to know.
        var attention = root.GetProperty("attention");
        Assert.Equal(JsonValueKind.Object, attention.ValueKind);
        Assert.Equal(0, attention.GetProperty("failedBackups").GetInt32());
    }

    /// <summary>
    /// One unreadable section does not empty the others: with no agent on this host, the resources
    /// are absent while the counts, which come from the database, are still there.
    /// </summary>
    /// <remarks>
    /// This is the behaviour the endpoint's per-section isolation exists for, and the test that
    /// would fail if a future change let one failing query take the response down with it. The
    /// integration host runs no agent, so "the agent cannot be reached" is the fixture's natural
    /// state rather than something this test has to arrange — which also means it would stop
    /// observing anything the day the suite gains an agent double, and it says so here rather than
    /// passing quietly.
    /// </remarks>
    [Fact]
    public async Task An_unreachable_agent_empties_its_own_section_and_leaves_the_rest()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("resources").ValueKind);
        Assert.Equal(1, root.GetProperty("counts").GetProperty("accounts").GetInt32());
    }

    /// <summary>Boots the panel against this test's own database.</summary>
    /// <returns>The booted host factory.</returns>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
        });
    }

    /// <summary>Applies the migrations of the contexts these tests read through.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
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
}
