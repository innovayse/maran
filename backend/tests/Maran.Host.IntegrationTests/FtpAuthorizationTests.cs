using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Ftp.Controllers;
using Maran.Modules.Ftp.Domain.Entities;
using Maran.Modules.Ftp.Persistence;
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
/// The FTPS surface over real HTTP against real PostgreSQL: the IDOR fixture the login endpoints are
/// required to have (rules/testing.md, Definition of Done 3), and the role gate the daemon endpoints
/// beside them carry instead.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two controllers with two different authorisation shapes, in one fixture, because the contrast
/// is the point.</b> <see cref="FtpUsersController"/> is open to any signed-in caller and is scoped
/// by the tenant query filter on <see cref="FtpDbContext"/>, so another customer's login must answer
/// <b>404</b>: a 403 would say "this login exists but is not yours", which is the fact an attacker
/// wanted — and one of these routes, aimed at somebody else's row, would not merely disclose that
/// but hand the caller a WORKING CREDENTIAL into another customer's files, because a password reset
/// returns a new password once. <see cref="FtpsServerController"/> has no tenant dimension at all —
/// there is one vsftpd on this machine — so it is gated by ROLE and answers <b>403</b>. Using 404
/// there would say a server-wide daemon "does not exist" to a caller who is simply not an
/// administrator, and using 403 on the login routes would turn an identifier into an oracle. The two
/// answers are asserted as the specific ones they are, never as "not 200".
/// </para>
/// <para>
/// <b>Every refusing assertion here has an inverse control</b>, because a gate mutated to refuse
/// everything passes every test that only ever hands it something it must reject: the customer who
/// reads their OWN login is answered 200 with the row, and the administrator who reads the daemon
/// status is answered 200 with the facts.
/// </para>
/// <para>
/// <b>The admin-only status carries a design decision this fixture pins rather than endorses.</b>
/// A customer cannot learn whether FTPS is enabled on the server they are hosted on — the whole of
/// <see cref="FtpsServerController"/> is <c>AdminOnly</c>, status included, because the status
/// answer carries a certificate path on the host, which is operator-facing text a customer must
/// never be shown (rules/security.md item 8). Whether a customer ought to be told "FTPS is
/// available" through some narrower answer is the owner's call and is not this fixture's to make;
/// what is this fixture's is that the behaviour shipped today is OBSERVED, so a change to it is a
/// deliberate one and not a silent one.
/// </para>
/// <para>
/// The agent is the only substitution, and only because it cannot be present: it is a separate root
/// process that installs and configures a vsftpd and creates system logins.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class FtpAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every route under <c>/api/v1/ftp-users</c> that names one login.</summary>
    /// <remarks>
    /// The list must be COMPLETE, and its completeness is asserted by
    /// <see cref="Every_ftps_scoped_route_on_the_controller_is_covered_by_the_idor_fixture"/> rather
    /// than trusted.
    /// </remarks>
    public static TheoryData<string, string> FtpUserScopedEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/ftp-users/{id}" },
            { "POST", "/api/v1/ftp-users/{id}/password" },
            { "DELETE", "/api/v1/ftp-users/{id}" },
        };
    }

    /// <summary>Every route under <c>/api/v1/ftp-users</c>, including the collection ones.</summary>
    public static TheoryData<string, string> AllFtpUserEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/ftp-users" },
            { "POST", "/api/v1/ftp-users" },
            { "GET", "/api/v1/ftp-users/{id}" },
            { "POST", "/api/v1/ftp-users/{id}/password" },
            { "DELETE", "/api/v1/ftp-users/{id}" },
        };
    }

    /// <summary>Every route under <c>/api/v1/ftps-server</c> — the daemon's own surface.</summary>
    /// <remarks>
    /// Completeness is asserted by
    /// <see cref="Every_ftps_server_route_on_the_controller_is_covered_by_the_role_fixture"/>: a new
    /// daemon route added without a row here would otherwise enjoy no proof that it is closed to a
    /// customer at all.
    /// </remarks>
    public static TheoryData<string, string> FtpsServerEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/ftps-server" },
            { "POST", "/api/v1/ftps-server/enable" },
            { "POST", "/api/v1/ftps-server/disable" },
        };
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public FtpAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every ftps login endpoint.</summary>
    [Theory]
    [MemberData(nameof(AllFtpUserEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_ftps_login_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, Substitute(path, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A customer reaching for another tenants ftps login is answered not found and never forbidden.</summary>
    [Theory]
    [MemberData(nameof(FtpUserScopedEndpoints))]
    public async Task A_customer_reaching_for_another_tenants_ftps_login_is_answered_not_found_and_never_forbidden(
        string method,
        string path)
    {
        // The SPECIFIC answer, not "some refusal". A test that accepted either 403 or 404 would pass
        // against the exact defect this asserts against — a distinct refusal confirms the neighbour's
        // login exists, and this branch has already shipped a cross-tenant destructive delete once.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, world.StrangerFtpUserId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An unknown ftps identifier answers not found rather than failing.</summary>
    [Theory]
    [MemberData(nameof(FtpUserScopedEndpoints))]
    public async Task An_unknown_ftps_identifier_answers_not_found_rather_than_failing(string method, string path)
    {
        // 404, never 500: an identifier the caller invented is answered by the handler's typed
        // NotFound, not by an unhandled failure that reveals the shape of what is behind it. And it
        // must be the SAME answer as another tenant's row, or the difference between them IS the
        // oracle the 404 was chosen to remove.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A customer deleting another tenants ftps login leaves that login in place.</summary>
    [Fact]
    public async Task A_customer_deleting_another_tenants_ftps_login_leaves_that_login_in_place()
    {
        // The status code is not the whole property. A 404 returned by a handler that had already
        // deleted the row would satisfy the theory above and still be the cross-tenant destructive
        // delete this branch has already found and fixed once, in both protocols. So the row itself
        // is read back afterwards, past the tenant filter, and must still be there.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.DeleteAsync($"/api/v1/ftp-users/{world.StrangerFtpUserId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var ftp = scope.ServiceProvider.GetRequiredService<FtpDbContext>();
        var survivors = await ftp.FtpUsers
            .IgnoreQueryFilters()
            .Where(row => row.Id == world.StrangerFtpUserId)
            .Select(row => row.FullName)
            .ToListAsync();

        Assert.Equal(["stranger_deploy"], survivors);
    }

    /// <summary>A customer deleting their own ftps login removes it.</summary>
    [Fact]
    public async Task A_customer_deleting_their_own_ftps_login_removes_it()
    {
        // The inverse control for the refusal above. A delete route that answered NotFound to
        // EVERYBODY — a handler that never resolved a row, a gate refusing every caller — would make
        // "another tenant's login survives" true for a reason that has nothing to do with tenancy,
        // and the test beside this one would be green against a panel where nobody can delete
        // anything. So the legitimate owner is driven down the same route and the row must GO.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.DeleteAsync($"/api/v1/ftp-users/{world.OwnFtpUserId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var ftp = scope.ServiceProvider.GetRequiredService<FtpDbContext>();
        var survivors = await ftp.FtpUsers
            .IgnoreQueryFilters()
            .Where(row => row.Id == world.OwnFtpUserId)
            .Select(row => row.FullName)
            .ToListAsync();

        Assert.Empty(survivors);
    }

    /// <summary>A customer reading their own ftps login is answered with it.</summary>
    [Fact]
    public async Task A_customer_reading_their_own_ftps_login_is_answered_with_it()
    {
        // The inverse control for every refusal above: a route that was simply broken, or a seed that
        // never ran, would make "not found" true of every request and every one of those theories
        // green. It also pins the protocol label the merged screen renders, on the wire.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync($"/api/v1/ftp-users/{world.OwnFtpUserId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("deploy", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("own_deploy", body.RootElement.GetProperty("fullName").GetString());
        Assert.Equal("Ftps", body.RootElement.GetProperty("protocol").GetString());
    }

    /// <summary>Listing ftps logins shows a customer only their own.</summary>
    [Fact]
    public async Task Listing_ftps_logins_shows_a_customer_only_their_own()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/ftp-users");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.EnumerateArray().Select(row =>
        {
            return row.GetProperty("fullName").GetString();
        }).ToList();

        Assert.Equal(["own_deploy"], names);
    }

    /// <summary>Two tenants both hold an ftps login named deploy because the names are prefixed.</summary>
    [Fact]
    public async Task Two_tenants_both_hold_an_ftps_login_named_deploy_because_the_names_are_prefixed()
    {
        // The prefix, proved against the real schema rather than an in-memory store that enforces no
        // index at all: `own_deploy` and `stranger_deploy` are two rows whose unique keys must both
        // be satisfiable, and both customers' `Name` is `deploy`. Without it the fixture above would
        // be seeding a state the shipped database cannot hold.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);

        using var scope = factory.Services.CreateScope();
        var ftp = scope.ServiceProvider.GetRequiredService<FtpDbContext>();
        var byName = await ftp.FtpUsers
            .IgnoreQueryFilters()
            .Where(row => row.Name == "deploy")
            .Select(row => row.FullName)
            .OrderBy(fullName => fullName)
            .ToListAsync();

        Assert.Equal(["own_deploy", "stranger_deploy"], byName);
    }

    /// <summary>An anonymous caller is refused by every ftps daemon endpoint.</summary>
    [Theory]
    [MemberData(nameof(FtpsServerEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_ftps_daemon_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed in customer is refused by every ftps daemon endpoint and told so plainly.</summary>
    [Theory]
    [MemberData(nameof(FtpsServerEndpoints))]
    public async Task A_signed_in_customer_is_refused_by_every_ftps_daemon_endpoint_and_told_so_plainly(
        string method,
        string path)
    {
        // 403, and deliberately not 404. The tenant answer — "not found" — exists so that an
        // identifier cannot be used as an oracle; there is no tenant here and no identifier to probe,
        // because there is one vsftpd on this machine and it is the server's, not a customer's. The
        // specific code is asserted rather than "any refusal", because the two codes mean different
        // things here and swapping them would be a real change of contract.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A refused customer learns nothing about the daemon from the refusal itself.</summary>
    [Fact]
    public async Task A_refused_customer_learns_nothing_about_the_daemon_from_the_refusal_itself()
    {
        // The status code says the request was refused; it does not say the refusal was SILENT. The
        // stub answers with a running daemon whose certificate lives at a real host path, so if any
        // of that reached the body of a 403 it would be a customer reading operator-facing text about
        // the server they are hosted on (rules/security.md item 8) — and the code above would still
        // be green.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/ftps-server");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("/etc/maran", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passivePortMin", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("certificate", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An administrator reading the ftps daemon status is answered with it.</summary>
    [Fact]
    public async Task An_administrator_reading_the_ftps_daemon_status_is_answered_with_it()
    {
        // The inverse control for the role gate. Without it, a policy that refused EVERY caller —
        // administrators included — would pass every theory above, and the surface would be dead
        // rather than protected. The body is read as well as the code, because a 200 carrying nothing
        // would satisfy the code alone.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/ftps-server");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("running").GetBoolean());
        Assert.Equal(30_000, body.RootElement.GetProperty("passivePortMin").GetInt32());
    }

    /// <summary>Every ftps scoped route on the controller is covered by the idor fixture.</summary>
    /// <remarks>
    /// The fixtures above are hand-written lists, and a hand-written list of routes goes stale the
    /// first time somebody adds a route. This reads the routes off
    /// <see cref="FtpUsersController"/> itself, so a new login-scoped endpoint fails HERE — naming
    /// itself — rather than quietly enjoying no IDOR coverage.
    /// </remarks>
    [Fact]
    public void Every_ftps_scoped_route_on_the_controller_is_covered_by_the_idor_fixture()
    {
        var declared = ControllerRoutes.Declared<FtpUsersController>();
        Assert.NotEmpty(declared);

        var scopedInFixture = RouteStrings(FtpUserScopedEndpoints());
        var allInFixture = RouteStrings(AllFtpUserEndpoints());

        var missingFromAll = declared.Where(route =>
        {
            return !allInFixture.Contains(route);
        }).ToList();

        var missingFromScoped = declared
            .Where(route =>
            {
                return ControllerRoutes.IsResourceScoped(route);
            })
            .Where(route =>
            {
                return !scopedInFixture.Contains(route);
            })
            .ToList();

        Assert.True(
            missingFromAll.Count == 0,
            "These FtpUsersController routes are absent from AllFtpUserEndpoints(): "
            + string.Join(", ", missingFromAll));
        Assert.True(
            missingFromScoped.Count == 0,
            "These ftps-scoped routes are absent from FtpUserScopedEndpoints(), so nothing proves "
            + "they answer 404 rather than 403 for another tenant: " + string.Join(", ", missingFromScoped));
    }

    /// <summary>Every ftps server route on the controller is covered by the role fixture.</summary>
    /// <remarks>
    /// The same completeness question asked of the daemon surface, whose gate is a role rather than
    /// a tenant filter: a new route here that nobody listed would ship with nothing proving it is
    /// closed to a customer.
    /// </remarks>
    [Fact]
    public void Every_ftps_server_route_on_the_controller_is_covered_by_the_role_fixture()
    {
        var declared = ControllerRoutes.Declared<FtpsServerController>();
        Assert.NotEmpty(declared);

        var inFixture = RouteStrings(FtpsServerEndpoints());
        var missing = declared.Where(route =>
        {
            return !inFixture.Contains(route);
        }).ToList();

        Assert.True(
            missing.Count == 0,
            "These FtpsServerController routes are absent from FtpsServerEndpoints(), so nothing "
            + "proves they are closed to a customer: " + string.Join(", ", missing));
    }

    /// <summary>Flattens a theory's rows into "METHOD /path" strings.</summary>
    /// <param name="rows">One of the fixtures above.</param>
    private static HashSet<string> RouteStrings(TheoryData<string, string> rows)
    {
        return rows
            .Select(row =>
            {
                return $"{row[0]} {row[1]}";
            })
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Substitutes a login id into a route template.</summary>
    /// <param name="path">The route template.</param>
    /// <param name="ftpUserId">The identifier to place in it.</param>
    private static string Substitute(string path, Guid ftpUserId)
    {
        return path.Replace("{id}", ftpUserId.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent substituted.</summary>
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

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentFtpsClient>(new StubAgentFtpsClient());
            });
        });
    }

    /// <summary>Applies every module's migrations, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<FtpDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds two accounts, their users, and one login each — both named <c>deploy</c>.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The identifiers the tests address.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var ftp = scope.ServiceProvider.GetRequiredService<FtpDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5, 3));
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

        // Written through a context resolved OUTSIDE a request, whose ICurrentUser is not a signed-in
        // customer, so the seed itself does no tenant separating — the filter under test is the only
        // thing that can. Both rows carry the SAME customer-facing name, which is the prefix's whole
        // point and which the schema's unique keys must accept.
        var ownFtpUser = NewFtpUser(own.Id, "own", now);
        var strangerFtpUser = NewFtpUser(stranger.Id, "stranger", now);
        ftp.FtpUsers.AddRange(ownFtpUser, strangerFtpUser);
        await ftp.SaveChangesAsync();

        return new SeededWorld(ownFtpUser.Id, strangerFtpUser.Id);
    }

    /// <summary>Builds one login row named <c>deploy</c> under <paramref name="username"/>.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="username">The account's system user name, which forms the prefix.</param>
    /// <param name="now">The creation instant, from the panel's clock.</param>
    private static FtpUser NewFtpUser(Guid accountId, string username, DateTimeOffset now)
    {
        return new FtpUser(Guid.NewGuid(), accountId, "deploy", $"{username}_deploy", now);
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

    /// <summary>Issues one request, giving each POST route a body its model binder accepts.</summary>
    /// <remarks>
    /// The bodies must be VALID. A route whose body fails validation answers 400, which would make an
    /// authorization theory pass without the request ever reaching the gate, or the handler whose
    /// tenant scoping, that is the thing under test.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = BodyFor(path);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Builds a valid request body for one POST route.</summary>
    /// <param name="path">The absolute path being posted to.</param>
    private static JsonContent BodyFor(string path)
    {
        if (path.EndsWith("/api/v1/ftp-users", StringComparison.Ordinal))
        {
            return JsonContent.Create(new { accountId = Guid.NewGuid(), name = "newlogin" });
        }

        if (path.EndsWith("/api/v1/ftps-server/enable", StringComparison.Ordinal))
        {
            return JsonContent.Create(new { hostname = "panel.example.com", passiveAddress = string.Empty });
        }

        return JsonContent.Create(new { });
    }

    /// <summary>The identifiers a seeded world hands to the tests.</summary>
    /// <param name="OwnFtpUserId">The login belonging to the signed-in customer.</param>
    /// <param name="StrangerFtpUserId">The login belonging to the other tenant.</param>
    private sealed record SeededWorld(Guid OwnFtpUserId, Guid StrangerFtpUserId);
}
