using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Cron.Controllers;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The IDOR fixture the cron surface is required to have (rules/testing.md, Definition of Done 3),
/// driven over real HTTP against real PostgreSQL: two accounts, and the question asked of every cron
/// route at once — does a customer naming another tenant's account get 404, and not 403.
/// </summary>
/// <remarks>
/// <b>The boundary under test is different in kind from every other module's, which is why this
/// fixture exists rather than being assumed covered by theirs.</b> Elsewhere the 404 is produced by a
/// tenant query filter on a <c>DbContext</c>: the row genuinely is not in the result set. This module
/// has no context and no rows. Every route names an ACCOUNT, and what makes another tenant's account
/// answer 404 is <c>IAccountDirectory</c> answering null inside each handler — a resolution, not a
/// filter, and one that a handler could forget to consult without any query filter noticing.
///
/// So this fixture asserts two things, not one: that the answer is 404 rather than 403 or 200, and
/// that the AGENT was never addressed by the other tenant's system user name. The second matters
/// because the failure this module could have is not "returned a row it should not have" but
/// "installed a cron job in somebody else's crontab", which no response body would reveal.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class CronAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every route the two cron controllers declare.</summary>
    /// <remarks>
    /// EVERY one of them is tenant-scoped, which is unlike the other modules' fixtures and is why
    /// this list is not split into "scoped" and "all". There is no cron route that does not name an
    /// account: the account is a query parameter on the reads and a body field on the writes,
    /// because an entry id means nothing until it is asked of one account's crontab.
    ///
    /// The list must be COMPLETE, and its completeness is asserted by
    /// <see cref="Every_cron_route_on_both_controllers_is_covered_by_the_idor_fixture"/> rather than
    /// trusted.
    ///
    /// A plain list walked inside one test, rather than the <c>[Theory]</c> rows the other IDOR
    /// fixtures use, and the reason is measured rather than stylistic: xUnit builds a new instance of
    /// a test class per test method, each instance takes its own database on the shared server, and
    /// each database is a separate Npgsql pool that holds its connections for the life of the
    /// process. Eight routes across four theories is thirty-two hosts, which pushed this assembly
    /// into <c>53300: sorry, too many clients already</c> — surfacing in whichever unrelated test
    /// happened to run when the ceiling was reached. Walking the routes inside one host costs four
    /// hosts instead of thirty-two, and the assertion helper below still reports every route that
    /// answered wrongly, by name, rather than stopping at the first.
    /// </remarks>
    /// <returns>The method and route template of every cron endpoint.</returns>
    public static IReadOnlyList<(string Method, string Path)> Endpoints()
    {
        return
        [
            ("GET", "/api/v1/cron-entries"),
            ("POST", "/api/v1/cron-entries"),
            ("PUT", "/api/v1/cron-entries/{entryId}"),
            ("POST", "/api/v1/cron-entries/{entryId}/enabled"),
            ("GET", "/api/v1/cron-entries/{entryId}/output"),
            ("DELETE", "/api/v1/cron-entries/{entryId}"),
            ("GET", "/api/v1/cron-environment"),
            ("PUT", "/api/v1/cron-environment"),
        ];
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public CronAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every cron endpoint.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_by_every_cron_endpoint()
    {
        var agent = new StubAgentCronClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        await AssertEveryEndpointAsync(client, Guid.NewGuid(), HttpStatusCode.Unauthorized);

        Assert.Empty(agent.AddressedAccounts);
    }

    /// <summary>A customer naming another tenants account is answered not found and never forbidden.</summary>
    [Fact]
    public async Task A_customer_naming_another_tenants_account_is_answered_not_found_and_never_forbidden()
    {
        var agent = new StubAgentCronClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        await AssertEveryEndpointAsync(client, world.StrangerAccountId, HttpStatusCode.NotFound);

        // The half a status code cannot show: the other tenant's crontab was never addressed at all,
        // so nothing was installed, read, switched or removed in it before the refusal.
        Assert.DoesNotContain("stranger", agent.AddressedAccounts);
    }

    /// <summary>An account identifier nobody owns is answered not found rather than failing.</summary>
    [Fact]
    public async Task An_account_identifier_nobody_owns_is_answered_not_found_rather_than_failing()
    {
        // 404, never 500: an identifier the caller invented is answered by the handler's typed
        // NotFound, not by an unhandled failure that reveals the shape of what is behind it. And it
        // must be the SAME answer another tenant's account gets, or the difference is the oracle.
        var agent = new StubAgentCronClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        await AssertEveryEndpointAsync(client, Guid.NewGuid(), HttpStatusCode.NotFound);
    }

    /// <summary>A customer naming their own account reaches their own crontab on every route.</summary>
    [Fact]
    public async Task A_customer_naming_their_own_account_reaches_their_own_crontab_on_every_route()
    {
        // Guards every assertion above from passing for the wrong reason: if the routes were simply
        // broken, or the seed never ran, "not found" would be true of every request ever made.
        var agent = new StubAgentCronClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var refused = new List<string>();
        foreach (var (method, path) in Endpoints())
        {
            var response = await SendAsync(client, method, path, world.OwnAccountId);
            if (response.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.Created))
            {
                refused.Add($"{method} {path} answered {(int)response.StatusCode}");
            }
        }

        Assert.True(
            refused.Count == 0,
            "These routes refused the caller's OWN account: " + string.Join("; ", refused));
        Assert.Equal(["own"], agent.AddressedAccounts.Distinct());
    }

    /// <summary>A listing returns the crontab the agent reported for the caller's own account.</summary>
    [Fact]
    public async Task A_listing_returns_the_crontab_the_agent_reported_for_the_callers_own_account()
    {
        // The panel keeps no rows, so this is the assertion that the customer sees the SERVER's
        // crontab rather than a panel memory of what it once installed.
        var agent = new StubAgentCronClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync($"/api/v1/cron-entries?accountId={world.OwnAccountId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entry = Assert.Single(body.RootElement.EnumerateArray().ToList());
        Assert.Equal(StubAgentCronClient.EntryId, entry.GetProperty("entryId").GetString());
        Assert.Equal("/usr/bin/backup --account own", entry.GetProperty("command").GetString());
        Assert.True(entry.GetProperty("enabled").GetBoolean());
    }

    /// <summary>A malformed entry identifier is refused before it can become a path on the host.</summary>
    [Fact]
    public async Task A_malformed_entry_identifier_is_refused_before_it_can_become_a_path_on_the_host()
    {
        // The agent turns an entry id into three paths under the account's home. The panel's own
        // rule refuses anything but a lowercase hyphenated uuid, and this is that rule asserted
        // through the real pipeline — a 400 from the validator, and no agent call at all.
        var agent = new StubAgentCronClient();
        await using var factory = CreateFactory(agent);
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.DeleteAsync(
            $"/api/v1/cron-entries/NOT-A-UUID?accountId={world.OwnAccountId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(agent.AddressedAccounts);
    }

    /// <summary>Every cron route on both controllers is covered by the idor fixture.</summary>
    /// <remarks>
    /// The fixture above is a hand-written list, and a hand-written list of routes goes stale the
    /// first time somebody adds a route. This reads the routes off both controllers themselves, so a
    /// new cron endpoint fails HERE — naming itself — rather than quietly enjoying no IDOR coverage.
    /// </remarks>
    [Fact]
    public void Every_cron_route_on_both_controllers_is_covered_by_the_idor_fixture()
    {
        var declared = ControllerRoutes.Declared<CronEntriesController>()
            .Concat(ControllerRoutes.Declared<CronEnvironmentController>())
            .ToList();
        Assert.NotEmpty(declared);

        var inFixture = Endpoints()
            .Select(endpoint =>
            {
                return $"{endpoint.Method} {endpoint.Path}";
            })
            .ToHashSet(StringComparer.Ordinal);

        var missing = declared.Where(route =>
        {
            return !inFixture.Contains(route);
        }).ToList();

        Assert.True(
            missing.Count == 0,
            "These cron routes are absent from Endpoints(), so nothing proves they answer 404 "
            + "rather than 403 for another tenant: " + string.Join(", ", missing));
    }

    /// <summary>Asserts that every cron endpoint answers one status for one account identifier.</summary>
    /// <remarks>
    /// Every route is tried before anything is asserted, and each wrong answer is reported with its
    /// own method and path. Failing on the first would hide the rest, and "one route answers 403"
    /// and "every route answers 403" are different defects that a first-failure assertion reports
    /// identically.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="accountId">The account to name on every route.</param>
    /// <param name="expected">The status every route must answer.</param>
    private static async Task AssertEveryEndpointAsync(
        HttpClient client,
        Guid accountId,
        HttpStatusCode expected)
    {
        var wrong = new List<string>();
        foreach (var (method, path) in Endpoints())
        {
            var response = await SendAsync(client, method, path, accountId);
            if (response.StatusCode != expected)
            {
                wrong.Add($"{method} {path} answered {(int)response.StatusCode}");
            }
        }

        Assert.True(
            wrong.Count == 0,
            $"These routes did not answer {(int)expected}: " + string.Join("; ", wrong));
    }

    /// <summary>Substitutes the stub's entry id into a route template.</summary>
    /// <param name="path">The route template.</param>
    private static string Substitute(string path)
    {
        return path.Replace("{entryId}", StubAgentCronClient.EntryId, StringComparison.Ordinal);
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent replaced.</summary>
    /// <param name="agent">The cron agent double every request is served by.</param>
    private WebApplicationFactory<Program> CreateFactory(IAgentCronClient agent)
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

            // The panel refuses to boot without them, and a host that does not boot fails every
            // theory below with an options error rather than with an answer about tenancy.
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            // The ONLY substitution: the agent is another process on a provisioned host and cannot
            // be present. Everything the panel itself does stays the shipped code.
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(agent);
            });
        });
    }

    /// <summary>Applies every migration this fixture needs, the way the installer does before first boot.</summary>
    /// <remarks>
    /// Identity and Accounts only, and no cron context: this module owns no schema, so there is
    /// nothing of its own to migrate. Its rows live in an account's crontab, which the agent double
    /// stands in for.
    /// </remarks>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds two accounts and their users.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The identifiers the tests address.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
        var own = new Account(Guid.NewGuid(), "own", "own.example.com", planId, now);
        var stranger = new Account(Guid.NewGuid(), "stranger", "stranger.example.com", planId, now);
        accounts.Accounts.AddRange(own, stranger);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        customer.AssignAccount(own.Id);
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();

        return new SeededWorld(own.Id, stranger.Id);
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

    /// <summary>Issues one request, carrying the account the way that route carries it.</summary>
    /// <remarks>
    /// The account travels in the query string on a read and in the body on a write, which is the
    /// module's own convention rather than this helper's: the bodies must be VALID, because a route
    /// whose body fails validation answers 400 and the request never reaches the handler whose
    /// account resolution is the thing under test.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The route template.</param>
    /// <param name="accountId">The account to name.</param>
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string method,
        string path,
        Guid accountId)
    {
        var url = Substitute(path);
        var carriesBody = method is "POST" or "PUT";
        if (!carriesBody)
        {
            url += $"?accountId={accountId}";
        }

        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (carriesBody)
        {
            request.Content = BodyFor(path, accountId);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Builds a valid request body for one write route.</summary>
    /// <param name="path">The route template being written to.</param>
    /// <param name="accountId">The account to name in the body.</param>
    private static JsonContent BodyFor(string path, Guid accountId)
    {
        if (path.EndsWith("/enabled", StringComparison.Ordinal))
        {
            return JsonContent.Create(new { accountId, enabled = true });
        }

        if (path.EndsWith("/cron-environment", StringComparison.Ordinal))
        {
            return JsonContent.Create(new
            {
                accountId,
                variables = new[] { new { name = "TZ", value = "UTC" } },
            });
        }

        return JsonContent.Create(new
        {
            accountId,
            schedule = new
            {
                minute = "0",
                hour = "3",
                dayOfMonth = "*",
                month = "*",
                dayOfWeek = "*",
            },
            command = "/usr/bin/backup",
        });
    }

    /// <summary>The identifiers a seeded world hands to the tests.</summary>
    /// <param name="OwnAccountId">The account belonging to the signed-in customer.</param>
    /// <param name="StrangerAccountId">The account belonging to the other tenant.</param>
    private sealed record SeededWorld(Guid OwnAccountId, Guid StrangerAccountId);
}
