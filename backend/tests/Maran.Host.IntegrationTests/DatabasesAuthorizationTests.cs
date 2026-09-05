using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Databases.Controllers;
using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Persistence;
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
/// The IDOR fixture the Databases surface is required to have (rules/testing.md, Definition of Done
/// 3), driven over real HTTP against real PostgreSQL: two customers, two databases, and the question
/// asked of every database-scoped route at once — does customer A reaching for customer B's database
/// get 404, and not 403.
/// </summary>
/// <remarks>
/// It has to be 404, and it matters more here than anywhere else in the product so far. A 403 says
/// "this database exists but is not yours", which is the fact an attacker wanted; and one of the
/// routes below, if it could be pointed at somebody else's row, would not merely disclose that —
/// it would hand the caller a WORKING CREDENTIAL on their data, because a password reset returns a
/// new password once. The distinction is not made by a check in a handler: it is made by the tenant
/// query filter on <see cref="DatabasesDbContext"/>, which means the row genuinely is not in the
/// result set.
///
/// This also exercises the prefix end to end against a real database, which no unit test can: two
/// accounts both hold a database named <c>shop</c>, and the schema's own unique indexes have to
/// accept that.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class DatabasesAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every route under <c>/api/v1/databases</c> that names one database.</summary>
    /// <remarks>
    /// The list must be COMPLETE, and its completeness is asserted by
    /// <see cref="Every_database_scoped_route_on_the_controller_is_covered_by_the_idor_fixture"/>
    /// rather than trusted.
    /// </remarks>
    public static TheoryData<string, string> DatabaseScopedEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/databases/{id}" },
            { "POST", "/api/v1/databases/{id}/password" },
            { "DELETE", "/api/v1/databases/{id}" },
        };
    }

    /// <summary>Every route under <c>/api/v1/databases</c>, including the collection ones.</summary>
    public static TheoryData<string, string> AllDatabaseEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/databases" },
            { "POST", "/api/v1/databases" },
            { "GET", "/api/v1/databases/{id}" },
            { "POST", "/api/v1/databases/{id}/password" },
            { "DELETE", "/api/v1/databases/{id}" },
        };
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public DatabasesAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every database endpoint.</summary>
    [Theory]
    [MemberData(nameof(AllDatabaseEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_database_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, Substitute(path, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A customer reaching for another tenants database is answered not found and never forbidden.</summary>
    [Theory]
    [MemberData(nameof(DatabaseScopedEndpoints))]
    public async Task A_customer_reaching_for_another_tenants_database_is_answered_not_found_and_never_forbidden(
        string method,
        string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, world.StrangerDatabaseId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An unknown database identifier answers not found rather than failing.</summary>
    [Theory]
    [MemberData(nameof(DatabaseScopedEndpoints))]
    public async Task An_unknown_database_identifier_answers_not_found_rather_than_failing(string method, string path)
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

    /// <summary>A customer reading their own database is answered with it.</summary>
    [Fact]
    public async Task A_customer_reading_their_own_database_is_answered_with_it()
    {
        // Guards the theories above from passing for the wrong reason: if the route were simply
        // broken, or the seed never ran, "not found" would be true of every request.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync($"/api/v1/databases/{world.OwnDatabaseId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("shop", body.RootElement.GetProperty("name").GetString());
        Assert.Equal("own_shop", body.RootElement.GetProperty("fullName").GetString());
    }

    /// <summary>Listing databases shows a customer only their own.</summary>
    [Fact]
    public async Task Listing_databases_shows_a_customer_only_their_own()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/databases");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.EnumerateArray().Select(row =>
        {
            return row.GetProperty("fullName").GetString();
        }).ToList();

        Assert.Equal(["own_shop"], names);
    }

    /// <summary>Two tenants both hold a database named shop because the names are prefixed.</summary>
    [Fact]
    public async Task Two_tenants_both_hold_a_database_named_shop_because_the_names_are_prefixed()
    {
        // The prefix, proved against the real schema rather than an in-memory store that enforces no
        // index at all: `own_shop` and `stranger_shop` are two rows whose unique keys must both be
        // satisfiable, and both customers' `Name` is `shop`.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);

        using var scope = factory.Services.CreateScope();
        var databases = scope.ServiceProvider.GetRequiredService<DatabasesDbContext>();
        var byName = await databases.Databases
            .IgnoreQueryFilters()
            .Where(row => row.Name == "shop")
            .Select(row => row.FullName)
            .OrderBy(fullName => fullName)
            .ToListAsync();

        Assert.Equal(["own_shop", "stranger_shop"], byName);
    }

    /// <summary>No column of the databases table holds anything a password could be stored in.</summary>
    [Fact]
    public async Task No_column_of_the_databases_table_holds_anything_a_password_could_be_stored_in()
    {
        // Asked of the SHIPPED schema in a real PostgreSQL, so it covers what the migration actually
        // created rather than what the model claims — the two are the same thing only while nobody
        // has hand-edited a migration.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);

        using var scope = factory.Services.CreateScope();
        var databases = scope.ServiceProvider.GetRequiredService<DatabasesDbContext>();
        var connection = databases.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();

        // raw-sql: the question is about the catalogue itself, which EF cannot model. Parameterized,
        // and reads nothing but column names.
        command.CommandText =
            "select column_name from information_schema.columns "
            + "where table_schema = @schema and table_name = @table";
        var schema = command.CreateParameter();
        schema.ParameterName = "schema";
        schema.Value = DatabasesDbContext.SchemaName;
        command.Parameters.Add(schema);
        var table = command.CreateParameter();
        table.ParameterName = "table";
        table.Value = "Databases";
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
                    || column.Contains("hash", StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>Every database scoped route on the controller is covered by the idor fixture.</summary>
    /// <remarks>
    /// The fixtures above are hand-written lists, and a hand-written list of routes goes stale the
    /// first time somebody adds a route. This reads the routes off
    /// <see cref="DatabasesController"/> itself, so a new database-scoped endpoint fails HERE —
    /// naming itself — rather than quietly enjoying no IDOR coverage.
    /// </remarks>
    [Fact]
    public void Every_database_scoped_route_on_the_controller_is_covered_by_the_idor_fixture()
    {
        var declared = ControllerRoutes.Declared<DatabasesController>();
        Assert.NotEmpty(declared);

        var scopedInFixture = RouteStrings(DatabaseScopedEndpoints());
        var allInFixture = RouteStrings(AllDatabaseEndpoints());

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
            "These DatabasesController routes are absent from AllDatabaseEndpoints(): "
            + string.Join(", ", missingFromAll));
        Assert.True(
            missingFromScoped.Count == 0,
            "These database-scoped routes are absent from DatabaseScopedEndpoints(), so nothing proves "
            + "they answer 404 rather than 403 for another tenant: " + string.Join(", ", missingFromScoped));
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

    /// <summary>Substitutes a database id into a route template.</summary>
    /// <param name="path">The route template.</param>
    /// <param name="databaseId">The identifier to place in it.</param>
    private static string Substitute(string path, Guid databaseId)
    {
        return path.Replace("{id}", databaseId.ToString(), StringComparison.Ordinal);
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
        await scope.ServiceProvider.GetRequiredService<DatabasesDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds two accounts, their users, and one database each — both named <c>shop</c>.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The identifiers the tests address.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var databases = scope.ServiceProvider.GetRequiredService<DatabasesDbContext>();
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
        var ownDatabase = NewDatabase(own.Id, "own", now);
        var strangerDatabase = NewDatabase(stranger.Id, "stranger", now);
        databases.Databases.AddRange(ownDatabase, strangerDatabase);
        await databases.SaveChangesAsync();

        return new SeededWorld(ownDatabase.Id, strangerDatabase.Id);
    }

    /// <summary>Builds one database row named <c>shop</c> under <paramref name="username"/>.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="username">The account's system user name, which forms the prefix.</param>
    /// <param name="now">The creation instant, from the panel's clock.</param>
    private static Database NewDatabase(Guid accountId, string username, DateTimeOffset now)
    {
        return new Database(
            Guid.NewGuid(), accountId, "shop", $"{username}_shop", $"{username}_shopuser", "shopuser", now);
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
        if (path.EndsWith("/api/v1/databases", StringComparison.Ordinal))
        {
            return JsonContent.Create(new { accountId = Guid.NewGuid(), name = "newdb", dbUserName = "newuser" });
        }

        return JsonContent.Create(new { });
    }

    /// <summary>The identifiers a seeded world hands to the tests.</summary>
    /// <param name="OwnDatabaseId">The database belonging to the signed-in customer.</param>
    /// <param name="StrangerDatabaseId">The database belonging to the other tenant.</param>
    private sealed record SeededWorld(Guid OwnDatabaseId, Guid StrangerDatabaseId);
}
