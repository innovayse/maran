using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Databases.Controllers;
using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Domain.Enums;
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
/// The IDOR fixture the database users surface is required to have (rules/testing.md, Definition of Done
/// 3), driven over real HTTP against real PostgreSQL: two customers, a database and its dedicated user
/// each, and the question asked of every user-scoped route at once — does customer A reaching for
/// customer B's user, or aiming a grant at customer B's database, get 404 and not 403.
/// </summary>
/// <remarks>
/// It has to be 404 for the reason <see cref="DatabasesAuthorizationTests"/> gives, and the password route
/// makes it matter: a reset on somebody else's user would return a working credential on their data. A
/// grant has no account id of its own and is scoped through its user, so this is also the proof that the
/// grant filter, not a handler's memory, keeps one tenant's privileges from another.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class DatabaseUsersAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every route under <c>/api/v1/database-users</c> that names one user.</summary>
    /// <remarks>Its completeness is asserted by <see cref="Every_user_scoped_route_on_the_controller_is_covered_by_the_idor_fixture"/>.</remarks>
    public static TheoryData<string, string> UserScopedEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/database-users/{id}" },
            { "POST", "/api/v1/database-users/{id}/password" },
            { "PUT", "/api/v1/database-users/{id}/grants/{databaseId}" },
            { "DELETE", "/api/v1/database-users/{id}" },
        };
    }

    /// <summary>Every route under <c>/api/v1/database-users</c>, including the collection ones.</summary>
    public static TheoryData<string, string> AllUserEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/database-users" },
            { "GET", "/api/v1/database-users/privilege-presets" },
            { "POST", "/api/v1/database-users" },
            { "GET", "/api/v1/database-users/{id}" },
            { "POST", "/api/v1/database-users/{id}/password" },
            { "PUT", "/api/v1/database-users/{id}/grants/{databaseId}" },
            { "DELETE", "/api/v1/database-users/{id}" },
        };
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public DatabaseUsersAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every database users endpoint.</summary>
    [Theory]
    [MemberData(nameof(AllUserEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_database_users_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, Substitute(path, Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A customer reaching for another tenants user is answered not found and never forbidden.</summary>
    [Theory]
    [MemberData(nameof(UserScopedEndpoints))]
    public async Task A_customer_reaching_for_another_tenants_user_is_answered_not_found_and_never_forbidden(
        string method,
        string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, world.StrangerUserId, world.OwnDatabaseId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An unknown user identifier answers not found, the same as another tenants row.</summary>
    [Theory]
    [MemberData(nameof(UserScopedEndpoints))]
    public async Task An_unknown_user_identifier_answers_not_found_rather_than_failing(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, Guid.NewGuid(), world.OwnDatabaseId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A customer aiming a grant of their own user at another tenants database is answered not found.</summary>
    [Fact]
    public async Task A_customer_aiming_their_own_users_grant_at_another_tenants_database_is_answered_not_found()
    {
        // The user is the caller's; the database is the stranger's. Without the filter on the database
        // this would be a cross-tenant grant, so the answer is the proof of the second row's scoping.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(
            client,
            "PUT",
            Substitute("/api/v1/database-users/{id}/grants/{databaseId}", world.OwnUserId, world.StrangerDatabaseId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A customer creating a user under another tenants account is answered not found.</summary>
    [Fact]
    public async Task A_customer_creating_a_user_under_another_tenants_account_is_answered_not_found()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.PostAsJsonAsync(
            "/api/v1/database-users", new { accountId = world.StrangerAccountId, nameSuffix = "intruder" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A customer reading their own user is answered with it, grants and choices included.</summary>
    [Fact]
    public async Task A_customer_reading_their_own_user_is_answered_with_it()
    {
        // Guards the theories above from passing for the wrong reason: if the route were broken or the
        // seed never ran, "not found" would be true of every request.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync($"/api/v1/database-users/{world.OwnUserId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("own_shopuser", body.RootElement.GetProperty("fullName").GetString());
        Assert.True(body.RootElement.GetProperty("isDedicated").GetBoolean());
        Assert.Equal("own_shop", body.RootElement.GetProperty("grants")[0].GetProperty("databaseFullName").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("grantableDatabases").GetArrayLength());
    }

    /// <summary>Listing users shows a customer only their own.</summary>
    [Fact]
    public async Task Listing_users_shows_a_customer_only_their_own()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/database-users");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.GetProperty("items").EnumerateArray()
            .Select(row => { return row.GetProperty("fullName").GetString(); })
            .ToList();

        Assert.Equal(["own_shopuser"], names);
    }

    /// <summary>
    /// The search term is matched literally against the full name on PostgreSQL: a fragment finds the user and
    /// a lone percent sign, which would match everything as a wildcard, finds nothing.
    /// </summary>
    [Fact]
    public async Task Searching_users_matches_the_name_literally()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var fragment = await client.GetAsync("/api/v1/database-users?search=shop");
        var wildcard = await client.GetAsync("/api/v1/database-users?search=%25");

        Assert.Equal(HttpStatusCode.OK, fragment.StatusCode);
        using var found = JsonDocument.Parse(await fragment.Content.ReadAsStringAsync());
        Assert.Equal(1, found.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK, wildcard.StatusCode);
        using var none = JsonDocument.Parse(await wildcard.Content.ReadAsStringAsync());
        Assert.Equal(0, none.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>A customer naming another tenants account in the filter is listed nothing.</summary>
    [Fact]
    public async Task A_customer_naming_another_tenants_account_is_listed_no_users()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync($"/api/v1/database-users?accountId={world.StrangerAccountId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>A customer reading the privilege catalogue is answered with the closed set and its presets.</summary>
    [Fact]
    public async Task A_customer_reading_the_privilege_catalogue_is_answered_with_the_closed_set_and_its_presets()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/database-users/privilege-presets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var keys = body.RootElement.GetProperty("privileges").EnumerateArray()
            .Select(row => { return row.GetProperty("key").GetString(); })
            .ToList();
        Assert.Equal(18, keys.Count);
        Assert.Contains("createTemporaryTables", keys);
        Assert.DoesNotContain(keys, key => { return key is "grantOption" or "file" or "super" or "process"; });
        Assert.Equal(3, body.RootElement.GetProperty("presets").GetArrayLength());
    }

    /// <summary>No column of the database users tables holds anything a password could be stored in.</summary>
    [Fact]
    public async Task No_column_of_the_database_users_tables_holds_anything_a_password_could_be_stored_in()
    {
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
            + "where table_schema = @schema and table_name in ('DatabaseUsers', 'DatabaseGrants')";
        var schema = command.CreateParameter();
        schema.ParameterName = "schema";
        schema.Value = DatabasesDbContext.SchemaName;
        command.Parameters.Add(schema);

        var columns = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(0));
            }
        }

        // The positive control: the probe found the tables, so an empty answer cannot pass it blind.
        Assert.Contains("NameSuffix", columns);
        Assert.Contains("Privileges", columns);
        Assert.DoesNotContain(
            columns,
            column =>
            {
                return column.Contains("password", StringComparison.OrdinalIgnoreCase)
                    || column.Contains("secret", StringComparison.OrdinalIgnoreCase)
                    || column.Contains("hash", StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>Every user scoped route on the controller is covered by the idor fixture.</summary>
    [Fact]
    public void Every_user_scoped_route_on_the_controller_is_covered_by_the_idor_fixture()
    {
        var declared = ControllerRoutes.Declared<DatabaseUsersController>();
        Assert.NotEmpty(declared);

        var scopedInFixture = RouteStrings(UserScopedEndpoints());
        var allInFixture = RouteStrings(AllUserEndpoints());

        var missingFromAll = declared.Where(route => { return !allInFixture.Contains(route); }).ToList();
        var missingFromScoped = declared
            .Where(route => { return ControllerRoutes.IsResourceScoped(route); })
            .Where(route => { return !scopedInFixture.Contains(route); })
            .ToList();

        Assert.True(
            missingFromAll.Count == 0,
            "These DatabaseUsersController routes are absent from AllUserEndpoints(): "
            + string.Join(", ", missingFromAll));
        Assert.True(
            missingFromScoped.Count == 0,
            "These user-scoped routes are absent from UserScopedEndpoints(), so nothing proves they "
            + "answer 404 rather than 403 for another tenant: " + string.Join(", ", missingFromScoped));
    }

    /// <summary>Flattens a theory's rows into "METHOD /path" strings.</summary>
    /// <param name="rows">One of the fixtures above.</param>
    private static HashSet<string> RouteStrings(TheoryData<string, string> rows)
    {
        return rows.Select(row => { return $"{row[0]} {row[1]}"; }).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Substitutes the identifiers into a route template.</summary>
    /// <param name="path">The route template.</param>
    /// <param name="userId">The user identifier to place in it.</param>
    /// <param name="databaseId">The database identifier to place in it.</param>
    private static string Substitute(string path, Guid userId, Guid databaseId)
    {
        return path
            .Replace("{id}", userId.ToString(), StringComparison.Ordinal)
            .Replace("{databaseId}", databaseId.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Boots the host against this class's PostgreSQL.</summary>
    private WebApplicationFactory<Program> CreateFactory()
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

    /// <summary>Seeds two accounts, their users, and one database with its dedicated user each.</summary>
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

        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        identity.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), customer.Id, own.Id, now));
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();

        // Written through a context resolved OUTSIDE a request, whose ICurrentUser is not a signed-in
        // customer, so the seed itself does no tenant separating: the filters under test are the only
        // thing that can.
        var ownWorld = NewDatabaseWithUser(own.Id, "own", now);
        var strangerWorld = NewDatabaseWithUser(stranger.Id, "stranger", now);
        databases.Databases.AddRange(ownWorld.Database, strangerWorld.Database);
        databases.DatabaseUsers.AddRange(ownWorld.User, strangerWorld.User);
        await databases.SaveChangesAsync();

        return new SeededWorld(
            ownWorld.User.Id, strangerWorld.User.Id, ownWorld.Database.Id, strangerWorld.Database.Id, stranger.Id);
    }

    /// <summary>Builds a database named <c>shop</c> and its dedicated user holding every privilege on it.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="username">The account's system user name, which forms the prefix.</param>
    /// <param name="now">The creation instant.</param>
    private static (Database Database, DatabaseUser User) NewDatabaseWithUser(
        Guid accountId,
        string username,
        DateTimeOffset now)
    {
        var database = new Database(
            Guid.NewGuid(), accountId, "shop", $"{username}_shop", $"{username}_shopuser", "shopuser", now);
        var user = new DatabaseUser(Guid.NewGuid(), accountId, "shopuser", $"{username}_shopuser", now);
        user.ReplacePrivileges(database.Id, DatabasePrivilegeSet.Select | DatabasePrivilegeSet.Insert, Guid.NewGuid(), now);

        return (database, user);
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

    /// <summary>Issues one request, giving each body-carrying route a body its model binder accepts.</summary>
    /// <remarks>
    /// The bodies must be VALID. A route whose body fails validation answers 400, which would make an
    /// authorization theory pass without the request ever reaching the handler whose tenant scoping is
    /// the thing under test.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = path.EndsWith("/api/v1/database-users", StringComparison.Ordinal)
                ? JsonContent.Create(new { accountId = Guid.NewGuid(), nameSuffix = "newuser" })
                : JsonContent.Create(new { });
        }
        else if (method == "PUT")
        {
            request.Content = JsonContent.Create(new { privileges = new List<string> { "select" } });
        }

        return await client.SendAsync(request);
    }

    /// <summary>The identifiers a seeded world hands to the tests.</summary>
    /// <param name="OwnUserId">The user belonging to the signed-in customer.</param>
    /// <param name="StrangerUserId">The user belonging to the other tenant.</param>
    /// <param name="OwnDatabaseId">The database belonging to the signed-in customer.</param>
    /// <param name="StrangerDatabaseId">The database belonging to the other tenant.</param>
    /// <param name="StrangerAccountId">The other tenant's account, which a customer may name in a filter.</param>
    private sealed record SeededWorld(
        Guid OwnUserId,
        Guid StrangerUserId,
        Guid OwnDatabaseId,
        Guid StrangerDatabaseId,
        Guid StrangerAccountId);
}
