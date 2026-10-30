using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The account resource summary over real HTTP against a real database: the administrator's
/// <c>GET /api/v1/accounts/{id}/resource-summary</c> and the customer's own
/// <c>GET /api/v1/accounts/me/resource-summary</c>.
/// </summary>
/// <remarks>
/// The administrator's route sits on the <c>AdminOnly</c> accounts controller, so a customer is
/// refused with 403 for EVERY id, their own included: the policy runs before anything reads the id,
/// so the refusal discloses nothing about whether another account exists (rules/security.md item 6
/// forbids a 403 only where it would). The customer's route carries no id at all — the account comes
/// from the token — so the IDOR-shaped question is answered by asserting a customer's summary names
/// their own account and counts only their own rows.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AccountResourceSummaryEndpointTests : IAsyncLifetime
{
    /// <summary>Every seeded login's password.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The encryption and signing key the host is booted with.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountResourceSummaryEndpointTests(PostgresFixture postgres)
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

    /// <summary>An administrator gets each counted kind beside its plan limit.</summary>
    [Fact]
    public async Task An_administrator_gets_each_counted_kind_beside_its_plan_limit()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var (olive, pepper) = await SeedTwoAccountsAsync(factory);
        await SeedSitesAsync(factory, olive, "one.olive.example.com", "two.olive.example.com");
        await SeedSitesAsync(factory, pepper, "one.pepper.example.com");
        await SeedAdministratorAsync(factory);

        using var client = await SignInAsync(factory, "admin");
        var response = await client.GetAsync($"/api/v1/accounts/{olive}/resource-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(olive, body.RootElement.GetProperty("accountId").GetGuid());

        var sites = Row(body, "sites");
        Assert.Equal("counted", sites.GetProperty("state").GetString());
        Assert.Equal(2, sites.GetProperty("used").GetInt32());
        Assert.Equal("limited", sites.GetProperty("limitKind").GetString());
        Assert.Equal(5, sites.GetProperty("limit").GetInt32());

        // The stub agent lists one entry for every account it is asked about.
        var cron = Row(body, "cronEntries");
        Assert.Equal("counted", cron.GetProperty("state").GetString());
        Assert.Equal(1, cron.GetProperty("used").GetInt32());

        var backups = Row(body, "backups");
        Assert.Equal(0, backups.GetProperty("used").GetInt32());
        Assert.Equal("unlimited", backups.GetProperty("limitKind").GetString());
    }

    /// <summary>An administrator asking about an unknown account gets 404.</summary>
    [Fact]
    public async Task An_administrator_asking_about_an_unknown_account_gets_not_found()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAdministratorAsync(factory);

        using var client = await SignInAsync(factory, "admin");
        var response = await client.GetAsync($"/api/v1/accounts/{Guid.NewGuid()}/resource-summary");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A customer is refused the administrator route for any id, their own included.</summary>
    [Fact]
    public async Task A_customer_is_refused_the_administrator_route_for_any_id_their_own_included()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var (olive, pepper) = await SeedTwoAccountsAsync(factory);
        await SeedCustomerAsync(factory, "olive", olive);

        using var client = await SignInAsync(factory, "olive");
        var other = await client.GetAsync($"/api/v1/accounts/{pepper}/resource-summary");
        var own = await client.GetAsync($"/api/v1/accounts/{olive}/resource-summary");
        var unknown = await client.GetAsync($"/api/v1/accounts/{Guid.NewGuid()}/resource-summary");

        Assert.Equal(HttpStatusCode.Forbidden, other.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unknown.StatusCode);
    }

    /// <summary>A customer reading their own summary gets their own account and only their own rows.</summary>
    [Fact]
    public async Task A_customer_reading_their_own_summary_gets_their_own_account_and_only_their_own_rows()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var (olive, pepper) = await SeedTwoAccountsAsync(factory);
        await SeedSitesAsync(factory, olive, "one.olive.example.com");
        await SeedSitesAsync(factory, pepper, "one.pepper.example.com", "two.pepper.example.com");
        await SeedCustomerAsync(factory, "olive", olive);

        using var client = await SignInAsync(factory, "olive");
        var response = await client.GetAsync("/api/v1/accounts/me/resource-summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(olive, body.RootElement.GetProperty("accountId").GetGuid());
        Assert.Equal(1, Row(body, "sites").GetProperty("used").GetInt32());
    }

    /// <summary>An administrator owns no account and gets 404 from the customer route.</summary>
    [Fact]
    public async Task An_administrator_owns_no_account_and_gets_not_found_from_the_customer_route()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAdministratorAsync(factory);

        using var client = await SignInAsync(factory, "admin");
        var response = await client.GetAsync("/api/v1/accounts/me/resource-summary");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>An anonymous caller is refused both routes.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_both_routes()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var admin = await client.GetAsync($"/api/v1/accounts/{Guid.NewGuid()}/resource-summary");
        var mine = await client.GetAsync("/api/v1/accounts/me/resource-summary");

        Assert.Equal(HttpStatusCode.Unauthorized, admin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, mine.StatusCode);
    }

    /// <summary>Finds the summary row of one kind.</summary>
    /// <param name="body">The summary response.</param>
    /// <param name="kind">The kind, as the wire spells it.</param>
    /// <returns>The row.</returns>
    private static JsonElement Row(JsonDocument body, string kind)
    {
        return body.RootElement.GetProperty("resources").EnumerateArray()
            .Single(row => { return row.GetProperty("kind").GetString() == kind; });
    }

    /// <summary>Boots the host against this class's PostgreSQL, with a stub cron agent.</summary>
    /// <returns>The factory.</returns>
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

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentCronClient>(new StubAgentCronClient());
            });
        });
    }

    /// <summary>Applies every composed module's migrations, since every counter reads its own schema.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>A task that completes when the schemas exist.</returns>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await ModuleSchemas.MigrateAsync(scope.ServiceProvider);
    }

    /// <summary>Creates two accounts on one plan selling five sites, and returns both their ids.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The two accounts' ids.</returns>
    private static async Task<(Guid First, Guid Second)> SeedTwoAccountsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        context.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));

        var first = new Account(Guid.NewGuid(), "olive", "olive.example.com", planId, now);
        var second = new Account(Guid.NewGuid(), "pepper", "pepper.example.com", planId, now);
        context.Accounts.AddRange(first, second);
        await context.SaveChangesAsync();

        return (first.Id, second.Id);
    }

    /// <summary>Writes site rows for one account straight into the Sites schema.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domains">One site per domain.</param>
    /// <returns>A task that completes when the rows are saved.</returns>
    private static async Task SeedSitesAsync(
        WebApplicationFactory<Program> factory, Guid accountId, params string[] domains)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        foreach (var domain in domains)
        {
            context.Sites.Add(new Site(
                Guid.NewGuid(),
                accountId,
                domain,
                [],
                SiteBackendType.Php,
                "8.3",
                string.Empty,
                $"/home/x/{domain}",
                now));
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Creates an administrator with the fixed username <c>admin</c>.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>A task that completes when the login is saved.</returns>
    private static async Task SeedAdministratorAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        context.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        await context.SaveChangesAsync();
    }

    /// <summary>Creates an already-active customer login owning <paramref name="accountId"/>.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login's name.</param>
    /// <param name="accountId">The account the login owns.</param>
    /// <returns>A task that completes when the login is saved.</returns>
    private static async Task SeedCustomerAsync(
        WebApplicationFactory<Program> factory, string username, Guid accountId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var customer = new User(
            Guid.NewGuid(), username, $"{username}@example.com", hasher.Hash(Password), UserRole.Customer, now);
        customer.AssignAccount(accountId);
        context.Users.Add(customer);
        await context.SaveChangesAsync();
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The login to sign in as.</param>
    /// <returns>A client carrying the login's bearer token.</returns>
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
