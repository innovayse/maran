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
using Maran.Modules.Sftp.Controllers;
using Maran.Modules.Sftp.Domain.Entities;
using Maran.Modules.Sftp.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The IDOR fixture the SFTP surface is required to have (rules/testing.md, Definition of Done 3),
/// driven over real HTTP against real PostgreSQL: two customers, two logins, and the question asked
/// of every login-scoped route at once — does customer A reaching for customer B's login get 404,
/// and not 403.
/// </summary>
/// <remarks>
/// It has to be 404. A 403 says "this login exists but is not yours", which is the fact an attacker
/// wanted; and one of the routes below, if it could be pointed at somebody else's row, would not
/// merely disclose that — it would hand the caller a WORKING CREDENTIAL into another customer's home
/// directory, because a password reset returns a new password once. The distinction is not made by a
/// check in a handler: it is made by the tenant query filter on <see cref="SftpDbContext"/>, which
/// means the row genuinely is not in the result set.
///
/// This also exercises the prefix end to end against a real database, which no unit test can: two
/// accounts both hold a login named <c>deploy</c>, and the schema's own unique indexes have to
/// accept that.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SftpAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every route under <c>/api/v1/sftp-users</c> that names one login.</summary>
    /// <remarks>
    /// The list must be COMPLETE, and its completeness is asserted by
    /// <see cref="Every_sftp_scoped_route_on_the_controller_is_covered_by_the_idor_fixture"/> rather
    /// than trusted.
    /// </remarks>
    public static TheoryData<string, string> SftpScopedEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/sftp-users/{id}" },
            { "POST", "/api/v1/sftp-users/{id}/password" },
            { "DELETE", "/api/v1/sftp-users/{id}" },
        };
    }

    /// <summary>Every route under <c>/api/v1/sftp-users</c>, including the collection ones.</summary>
    public static TheoryData<string, string> AllSftpEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/sftp-users" },
            { "POST", "/api/v1/sftp-users" },
            { "GET", "/api/v1/sftp-users/{id}" },
            { "POST", "/api/v1/sftp-users/{id}/password" },
            { "DELETE", "/api/v1/sftp-users/{id}" },
        };
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SftpAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every sftp endpoint.</summary>
    [Theory]
    [MemberData(nameof(AllSftpEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_sftp_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, Substitute(path, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A customer reaching for another tenants sftp user is answered not found and never forbidden.</summary>
    [Theory]
    [MemberData(nameof(SftpScopedEndpoints))]
    public async Task A_customer_reaching_for_another_tenants_sftp_user_is_answered_not_found_and_never_forbidden(
        string method,
        string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, world.StrangerSftpUserId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An unknown sftp identifier answers not found rather than failing.</summary>
    [Theory]
    [MemberData(nameof(SftpScopedEndpoints))]
    public async Task An_unknown_sftp_identifier_answers_not_found_rather_than_failing(string method, string path)
    {
        // 404, never 500: an identifier the caller invented is answered by the handler's typed
        // NotFound, not by an unhandled failure that reveals the shape of what is behind it. And it
        // must be the SAME answer as another tenant's row, or the difference is the oracle.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A customer reading their own sftp user is answered with it.</summary>
    [Fact]
    public async Task A_customer_reading_their_own_sftp_user_is_answered_with_it()
    {
        // Guards the theories above from passing for the wrong reason: if the route were simply
        // broken, or the seed never ran, "not found" would be true of every request.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync($"/api/v1/sftp-users/{world.OwnSftpUserId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("deploy", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("own_deploy", body.RootElement.GetProperty("fullName").GetString());
    }

    /// <summary>Listing sftp users shows a customer only their own.</summary>
    [Fact]
    public async Task Listing_sftp_users_shows_a_customer_only_their_own()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/sftp-users");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.EnumerateArray().Select(row =>
        {
            return row.GetProperty("fullName").GetString();
        }).ToList();

        Assert.Equal(["own_deploy"], names);
    }

    /// <summary>Two tenants both hold an sftp user named deploy because the names are prefixed.</summary>
    [Fact]
    public async Task Two_tenants_both_hold_an_sftp_user_named_deploy_because_the_names_are_prefixed()
    {
        // The prefix, proved against the real schema rather than an in-memory store that enforces no
        // index at all: `own_deploy` and `stranger_deploy` are two rows whose unique keys must both
        // be satisfiable, and both customers' `Name` is `deploy`.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);

        using var scope = factory.Services.CreateScope();
        var sftp = scope.ServiceProvider.GetRequiredService<SftpDbContext>();
        var byName = await sftp.SftpUsers
            .IgnoreQueryFilters()
            .Where(row => row.Name == "deploy")
            .Select(row => row.FullName)
            .OrderBy(fullName => fullName)
            .ToListAsync();

        Assert.Equal(["own_deploy", "stranger_deploy"], byName);
    }

    /// <summary>No column of the sftp users table holds a password or a caller supplied jail.</summary>
    [Fact]
    public async Task No_column_of_the_sftp_users_table_holds_a_password_or_a_caller_supplied_jail()
    {
        // Asked of the SHIPPED schema in a real PostgreSQL, so it covers what the migration actually
        // created rather than what the model claims — the two are the same thing only while nobody
        // has hand-edited a migration. The jail half matters as much as the password half: the
        // chroot is `%h`, derived by the agent from the account, so a path column here would mean the
        // panel had started letting a customer name the directory they are confined to.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);

        using var scope = factory.Services.CreateScope();
        var sftp = scope.ServiceProvider.GetRequiredService<SftpDbContext>();
        var connection = sftp.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        // raw-sql: the question is about the catalogue itself, which EF cannot model. Parameterized,
        // and reads nothing but column names.
        command.CommandText =
            "select column_name from information_schema.columns "
            + "where table_schema = @schema and table_name = @table";
        var schema = command.CreateParameter();
        schema.ParameterName = "schema";
        schema.Value = SftpDbContext.SchemaName;
        command.Parameters.Add(schema);
        var table = command.CreateParameter();
        table.ParameterName = "table";
        table.Value = "SftpUsers";
        command.Parameters.Add(table);

        var columns = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }

        Assert.NotEmpty(columns);
        Assert.DoesNotContain(
            columns,
            column =>
            {
                return column.Contains("password", StringComparison.OrdinalIgnoreCase)
                    || column.Contains("secret", StringComparison.OrdinalIgnoreCase)
                    || column.Contains("hash", StringComparison.OrdinalIgnoreCase)
                    || column.Contains("chroot", StringComparison.OrdinalIgnoreCase)
                    || column.Contains("jail", StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>Every sftp scoped route on the controller is covered by the idor fixture.</summary>
    /// <remarks>
    /// The fixtures above are hand-written lists, and a hand-written list of routes goes stale the
    /// first time somebody adds a route. This reads the routes off
    /// <see cref="SftpUsersController"/> itself, so a new login-scoped endpoint fails HERE — naming
    /// itself — rather than quietly enjoying no IDOR coverage.
    /// </remarks>
    [Fact]
    public void Every_sftp_scoped_route_on_the_controller_is_covered_by_the_idor_fixture()
    {
        var declared = ControllerRoutes.Declared<SftpUsersController>();
        Assert.NotEmpty(declared);

        var scopedInFixture = RouteStrings(SftpScopedEndpoints());
        var allInFixture = RouteStrings(AllSftpEndpoints());

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
            "These SftpUsersController routes are absent from AllSftpEndpoints(): "
            + string.Join(", ", missingFromAll));
        Assert.True(
            missingFromScoped.Count == 0,
            "These sftp-scoped routes are absent from SftpScopedEndpoints(), so nothing proves they "
            + "answer 404 rather than 403 for another tenant: " + string.Join(", ", missingFromScoped));
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
    /// <param name="sftpUserId">The identifier to place in it.</param>
    private static string Substitute(string path, Guid sftpUserId)
    {
        return path.Replace("{id}", sftpUserId.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Boots the host against this class's PostgreSQL.</summary>
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
        });
    }

    /// <summary>Applies every module's migrations, the way the installer does before first boot.</summary>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<SftpDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds two accounts, their users, and one login each — both named <c>deploy</c>.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The identifiers the tests address.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var sftp = scope.ServiceProvider.GetRequiredService<SftpDbContext>();
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

        // Written through a context resolved OUTSIDE a request, whose ICurrentUser is not a signed-in
        // customer, so the seed itself does no tenant separating — the filter under test is the only
        // thing that can. Both rows carry the SAME customer-facing name, which is the prefix's whole
        // point and which the schema's unique keys must accept.
        var ownSftpUser = NewSftpUser(own.Id, "own", now);
        var strangerSftpUser = NewSftpUser(stranger.Id, "stranger", now);
        sftp.SftpUsers.AddRange(ownSftpUser, strangerSftpUser);
        await sftp.SaveChangesAsync();

        return new SeededWorld(ownSftpUser.Id, strangerSftpUser.Id);
    }

    /// <summary>Builds one login row named <c>deploy</c> under <paramref name="username"/>.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="username">The account's system user name, which forms the prefix.</param>
    /// <param name="now">The creation instant, from the panel's clock.</param>
    private static SftpUser NewSftpUser(Guid accountId, string username, DateTimeOffset now)
    {
        return new SftpUser(Guid.NewGuid(), accountId, "deploy", $"{username}_deploy", now);
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
    /// authorization theory pass without the request ever reaching the handler whose tenant scoping
    /// is the thing under test.
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
        if (path.EndsWith("/api/v1/sftp-users", StringComparison.Ordinal))
        {
            return JsonContent.Create(new { accountId = Guid.NewGuid(), name = "newlogin" });
        }

        return JsonContent.Create(new { });
    }

    /// <summary>The identifiers a seeded world hands to the tests.</summary>
    /// <param name="OwnSftpUserId">The login belonging to the signed-in customer.</param>
    /// <param name="StrangerSftpUserId">The login belonging to the other tenant.</param>
    private sealed record SeededWorld(Guid OwnSftpUserId, Guid StrangerSftpUserId);
}
