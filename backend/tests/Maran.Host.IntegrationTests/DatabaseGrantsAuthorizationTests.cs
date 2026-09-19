using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Databases.Controllers;
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
/// The grant-repair surface over real HTTP: who may read a host-wide list of grants, who may rewrite
/// them, and what a refused caller learns from the refusal.
/// </summary>
/// <remarks>
/// <para>
/// <b>It answers 403 and deliberately not 404, and the contrast with the sibling fixture is the
/// point.</b> <c>DatabasesAuthorizationTests</c> asserts 404 for another customer's database, because
/// there an identifier could be used as an oracle and the tenant query filter is what hides the row.
/// There is no identifier here and no tenant: one database server has one grant table, the subject is
/// every account on the host at once, and no query filter stands behind the endpoint at all. So the
/// role policy IS the authorisation, and 404 would be a lie about a server-wide table that plainly
/// exists.
/// </para>
/// <para>
/// <b>What makes this more than a role test.</b> A refused row on this surface carries the database
/// server's raw columns, and a row is refused precisely because the panel did not write it — so the
/// answer names identifiers the reader does not own. The double therefore answers with a refused row
/// belonging to NEITHER seeded tenant, and one of the assertions below is that none of those names
/// reaches the body of a customer's 403. A double that reported an empty census would have made that
/// assertion vacuous: the body would have had nothing to leak.
/// </para>
/// <para>
/// <b>Every refusing assertion has an inverse control</b>, because a gate mutated to refuse everything
/// passes every test that only hands it something it must reject: the administrator reading the report
/// is answered 200 with the census, and the administrator confirming the figure the report gave is
/// answered 200 with a repair that ran.
/// </para>
/// <para>
/// The agent is the only substitution, and only because it cannot be present: it is a separate root
/// process holding a socket to the host's database server.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class DatabaseGrantsAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every route under <c>/api/v1/database-grants</c>.</summary>
    /// <remarks>
    /// Completeness is asserted by
    /// <see cref="Every_grant_route_on_the_controller_is_covered_by_the_role_fixture"/>: a new route
    /// added without a row here would otherwise enjoy no proof that it is closed to a customer at all.
    /// </remarks>
    public static TheoryData<string, string> GrantEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/database-grants" },
            { "POST", "/api/v1/database-grants/repair" },
        };
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public DatabaseGrantsAuthorizationTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <remarks>
    /// Nothing to release: the shared-server fixture reclaims the per-test database itself, exactly as
    /// the sibling authorization fixtures leave it.
    /// </remarks>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>An anonymous caller is refused by every grant route.</summary>
    [Theory]
    [MemberData(nameof(GrantEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_grant_route(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed in customer is refused by every grant route and told so plainly.</summary>
    [Theory]
    [MemberData(nameof(GrantEndpoints))]
    public async Task A_signed_in_customer_is_refused_by_every_grant_route_and_told_so_plainly(
        string method,
        string path)
    {
        // 403, and deliberately not 404. The tenant answer exists so an identifier cannot be used as
        // an oracle; there is no identifier here and no tenant, because one database server has one
        // grant table and it is the server's, not a customer's. The specific code is asserted rather
        // than "any refusal", because the two mean different things and swapping them would be a real
        // change of contract.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A refused customer learns no other tenant's names from the refusal itself.</summary>
    [Fact]
    public async Task A_refused_customer_learns_no_other_tenants_names_from_the_refusal_itself()
    {
        // The status code says the request was refused; it does not say the refusal was SILENT. The
        // double answers with a refused row belonging to neither seeded tenant and a repairable row
        // whose old pattern reached a third database, so if any of that reached the body of a 403 it
        // would be a customer reading the names of databases they do not own — and the code assertion
        // above would still be green.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/database-grants");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain(StubAgentDbClient.StrangerDatabase, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StubAgentDbClient.StrangerUser, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(StubAgentDbClient.ExposedDatabase, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("examinedGrants", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An administrator reading the report is answered with the census.</summary>
    [Fact]
    public async Task An_administrator_reading_the_report_is_answered_with_the_census()
    {
        // The inverse control for the role gate. Without it, a policy that refused EVERY caller —
        // administrators included — would pass every theory above and the surface would be dead rather
        // than protected. The BODY is read as well as the code, because a 200 carrying nothing would
        // satisfy the code alone; and the refusal's two localized sentences are asserted to differ from
        // the machine reason, because that is the whole of what the panel adds over the agent.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/database-grants");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("isReportOnly").GetBoolean());
        Assert.Equal(3, body.RootElement.GetProperty("examinedGrants").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("wouldRepair").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("repaired").GetArrayLength());

        var refused = body.RootElement.GetProperty("refused")[0];
        Assert.Equal("NotThePanelsNaming", refused.GetProperty("reason").GetString());
        Assert.NotEqual("NotThePanelsNaming", refused.GetProperty("reasonDisplayName").GetString());
        Assert.NotEqual("NotThePanelsNaming", refused.GetProperty("reasonAdvice").GetString());
    }

    /// <summary>An administrator confirming the figure the report gave gets a repair that ran.</summary>
    [Fact]
    public async Task An_administrator_confirming_the_figure_the_report_gave_gets_a_repair_that_ran()
    {
        // The second inverse control, on the confirmation gate rather than on the role: a gate that
        // refused every figure would pass the staleness test below while making the repair unreachable.
        // `isReportOnly: false` is the assertion with teeth — it says an ACTION was performed rather
        // than another inspection returned.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync(
            "/api/v1/database-grants/repair", new { expectedRepairCount = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("isReportOnly").GetBoolean());
        Assert.Equal(1, body.RootElement.GetProperty("repaired").GetArrayLength());
        Assert.Equal(0, body.RootElement.GetProperty("wouldRepair").GetArrayLength());
    }

    /// <summary>An administrator who confirms a figure the host no longer matches is refused.</summary>
    [Fact]
    public async Task An_administrator_who_confirms_a_figure_the_host_no_longer_matches_is_refused()
    {
        // The gate that makes the report the only way in, observed on the wire: a caller who read no
        // report sends the default zero, which does not match the one row this host would rewrite. The
        // answer is 409 — nothing was changed, and retyping the request changes nothing — and the
        // message is the backend's own localized sentence, which the SPA renders verbatim.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync(
            "/api/v1/database-grants/repair", new { expectedRepairCount = 0 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DatabaseGrantRepairPlanChanged", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>Every grant route on the controller is covered by the role fixture.</summary>
    /// <remarks>
    /// The fixture above is a hand-written list, and a hand-written list of routes goes stale the first
    /// time somebody adds a route. This reads them off <see cref="DatabaseGrantsController"/> itself, so
    /// a new route fails HERE — naming itself — rather than quietly enjoying no proof that it is closed
    /// to a customer.
    /// </remarks>
    [Fact]
    public void Every_grant_route_on_the_controller_is_covered_by_the_role_fixture()
    {
        var declared = ControllerRoutes.Declared<DatabaseGrantsController>();
        Assert.NotEmpty(declared);

        var inFixture = GrantEndpoints()
            .Select(row =>
            {
                return $"{row[0]} {row[1]}";
            })
            .ToHashSet(StringComparer.Ordinal);

        var missing = declared.Where(route =>
        {
            return !inFixture.Contains(route);
        }).ToList();

        Assert.True(
            missing.Count == 0,
            "These DatabaseGrantsController routes are absent from GrantEndpoints(), so nothing proves "
            + "they are closed to a customer: " + string.Join(", ", missing));
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the database agent substituted.</summary>
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

            // Startup validation refuses to boot without the host's SSH ports and the panel's public
            // port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentDbClient>(new StubAgentDbClient());
            });
        });
    }

    /// <summary>Applies the migrations the surfaces under test need.</summary>
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

    /// <summary>Signs in over the real endpoint and returns a client carrying the access token.</summary>
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

    /// <summary>Issues one request, giving the POST route a body its model binder accepts.</summary>
    /// <remarks>
    /// The body must be VALID. A route whose body fails binding answers 400, which would make an
    /// authorization theory pass without the request ever reaching the gate that is the thing under
    /// test.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { expectedRepairCount = 1 });
        }

        return await client.SendAsync(request);
    }
}
