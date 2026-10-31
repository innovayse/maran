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
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// A site carries at most <see cref="SiteAliasLimit.MaximumAliases"/> aliases — every name one
/// certificate can cover but the domain's own — over real HTTP: a request at the limit is created, one
/// alias over is refused 400 with its own code and never reaches the agent, and the limit the site
/// form shows is the server's (issue #275). That an anonymous caller is refused the limits read is
/// asserted with every other site route by <see cref="SitesAuthorizationTests"/>.
/// </summary>
/// <remarks>
/// Definition of Done: item 2 (the real surface) and item 5 (the refusal is translated, asserted in
/// all three languages). Item 3 does not apply: the limits read is server-wide reference data with no
/// tenant row behind it, and the 404 for another tenant's account on site creation stays asserted by
/// <see cref="SitesAuthorizationTests"/>. Item 4 does not apply: the refusal is a validator's, raised
/// before any handler runs, and no validation refusal is journalled.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SiteAliasLimitEndpointTests : IAsyncLifetime
{
    /// <summary>The password every seeded user signs in with.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The key the host's encryption and token signing are configured with.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SiteAliasLimitEndpointTests(PostgresFixture postgres)
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

    /// <summary>A site carrying exactly the maximum number of aliases is created, and reaches the agent.</summary>
    [Fact]
    public async Task Posting_a_site_with_exactly_the_maximum_number_of_aliases_is_created()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await PostSiteAsync(client, world.AccountId, Aliases(SiteAliasLimit.MaximumAliases));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, agent.CreateCalls);
    }

    /// <summary>One alias over the maximum is answered 400 with its own code and never reaches the agent.</summary>
    [Fact]
    public async Task Posting_a_site_with_one_alias_over_the_maximum_is_answered_400_and_never_reaches_the_agent()
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await PostSiteAsync(client, world.AccountId, Aliases(SiteAliasLimit.MaximumAliases + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("SiteAliasLimitExceeded", await CodeOfAsync(response));
        Assert.Equal(0, agent.CreateCalls);
    }

    /// <summary>The alias-limit refusal is written in the language the panel asked for.</summary>
    /// <param name="language">The <c>Accept-Language</c> the panel sends.</param>
    /// <param name="expected">That language's own resx value.</param>
    [Theory]
    [InlineData("en", "A site cannot have more additional hostnames than one certificate can cover.")]
    [InlineData("ru", "У сайта не может быть больше дополнительных имён, чем покрывает один сертификат.")]
    [InlineData("hy", "Կայքը չի կարող ունենալ ավելի շատ լրացուցիչ անուններ, քան ընդգրկում է մեկ վկայականը։")]
    public async Task The_alias_limit_refusal_is_written_in_the_language_the_panel_asked_for(
        string language, string expected)
    {
        var agent = new BarrierSitesAgent(expectedCallers: 1);
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue(language));

        var response = await PostSiteAsync(client, world.AccountId, Aliases(SiteAliasLimit.MaximumAliases + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, body.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>The limits read answers the server's own alias maximum to a signed-in customer.</summary>
    [Fact]
    public async Task The_limits_read_answers_the_servers_alias_maximum()
    {
        await using var factory = CreateFactory(new BarrierSitesAgent(expectedCallers: 1));
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/sites/limits");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(SiteAliasLimit.MaximumAliases, body.RootElement.GetProperty("maximumAliases").GetInt32());
    }

    /// <summary>Distinct, well-formed aliases of the given count, none of them the posted domain.</summary>
    /// <param name="count">How many aliases to build.</param>
    /// <returns>The aliases.</returns>
    private static string[] Aliases(int count)
    {
        return Enumerable.Range(0, count).Select(index =>
        {
            return $"alias{index}.shop.example.com";
        }).ToArray();
    }

    /// <summary>Posts one static site creation for <c>shop.example.com</c>.</summary>
    /// <param name="client">A signed-in client.</param>
    /// <param name="accountId">The account the site is for.</param>
    /// <param name="aliases">The aliases.</param>
    private static Task<HttpResponseMessage> PostSiteAsync(HttpClient client, Guid accountId, string[] aliases)
    {
        return client.PostAsJsonAsync("/api/v1/sites", new
        {
            accountId,
            domain = "shop.example.com",
            aliases,
            backendType = 1,
            phpVersion = string.Empty,
            proxyUpstream = string.Empty,
        });
    }

    /// <summary>Reads the machine-stable <c>code</c> member out of a problem document.</summary>
    /// <param name="response">The refused response.</param>
    /// <returns>The error code, which is the resource key.</returns>
    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the sites agent replaced.</summary>
    /// <param name="agent">The agent double every site call reaches.</param>
    private WebApplicationFactory<Program> CreateFactory(
        BarrierSitesAgent agent)
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
                services.AddSingleton<IAgentSitesClient>(agent);
            });
        });
    }

    /// <summary>Applies every module's migrations these tests need, the way the installer does.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SitesDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SslDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds one administrator and one customer owning an account on a roomy plan.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The customer's account identifier.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        var account = new Account(Guid.NewGuid(), "customer", "customer.example.com", planId, now);
        accounts.Accounts.Add(account);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        customer.AssignAccount(account.Id);
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();

        return new SeededWorld(account.Id);
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>The identifiers a seeded world hands to the tests.</summary>
    /// <param name="AccountId">The customer's account.</param>
    private sealed record SeededWorld(Guid AccountId);
}
