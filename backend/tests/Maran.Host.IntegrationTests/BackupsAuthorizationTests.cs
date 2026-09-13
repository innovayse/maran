using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Backups.Controllers;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
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
/// The IDOR fixture the backups surface is required to have (rules/testing.md, Definition of Done 2
/// and 3), driven over real HTTP against real PostgreSQL: two customers, one backup each, and the
/// question asked of every backup-scoped route at once — does customer A reaching for customer B's
/// backup get 404, and not 403.
/// </summary>
/// <remarks>
/// It has to be 404. A 403 says "this backup exists but is not yours", and a backup identifier is
/// worth confirming: the same identifier names the artifact on the destination, so an attacker who
/// could enumerate them has learned the file names of every customer's archive. And one of the
/// routes below is a DELETE — pointed at somebody else's row it would not merely disclose the row,
/// it would destroy another customer's only copy of their data. The distinction is not made by a
/// check in a handler: it is made by the tenant query filter on <see cref="BackupsDbContext"/>, so
/// the row genuinely is not in the result set.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class BackupsAuthorizationTests : IAsyncLifetime
{
    /// <summary>The password every seeded login is given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>A base64 key long enough for the host's startup validation of both key settings.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The database this test class owns on the shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public BackupsAuthorizationTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Every route under <c>/api/v1/backups</c> that names one backup.</summary>
    /// <remarks>
    /// The list must be COMPLETE, and its completeness is asserted by
    /// <see cref="Every_backup_scoped_route_on_the_controller_is_covered_by_the_idor_fixture"/>
    /// rather than trusted.
    /// </remarks>
    /// <returns>The method and template of each backup-scoped route.</returns>
    public static TheoryData<string, string> BackupScopedEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/backups/{id}" },
            { "DELETE", "/api/v1/backups/{id}" },
            { "POST", "/api/v1/backups/{id}/restore" },
        };
    }

    /// <summary>Every route under <c>/api/v1/backups</c>, including the collection ones.</summary>
    /// <returns>The method and template of each route.</returns>
    public static TheoryData<string, string> AllBackupEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/backups" },
            { "POST", "/api/v1/backups" },
            { "GET", "/api/v1/backups/{id}" },
            { "DELETE", "/api/v1/backups/{id}" },
            { "POST", "/api/v1/backups/{id}/restore" },
        };
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>The task that creates this class's database.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>A completed task; the shared server outlives this class.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>An anonymous caller is refused by every backup endpoint.</summary>
    /// <param name="method">The HTTP method under test.</param>
    /// <param name="path">The route template under test.</param>
    [Theory]
    [MemberData(nameof(AllBackupEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_backup_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, Substitute(path, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A customer reaching for another tenants backup is answered not found and never forbidden.</summary>
    /// <param name="method">The HTTP method under test.</param>
    /// <param name="path">The route template under test.</param>
    [Theory]
    [MemberData(nameof(BackupScopedEndpoints))]
    public async Task A_customer_reaching_for_another_tenants_backup_is_answered_not_found_and_never_forbidden(
        string method,
        string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, world.StrangerBackupId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);

        // The row is still there. A 404 answered after the artifact and the row had been removed
        // would satisfy the status assertion and would still have destroyed another customer's
        // backup, so the refusal is measured by its effect and not only by its answer.
        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        Assert.True(await backups.Backups.IgnoreQueryFilters().AnyAsync(row =>
            row.Id == world.StrangerBackupId));
    }

    /// <summary>An unknown backup identifier answers not found rather than failing.</summary>
    /// <param name="method">The HTTP method under test.</param>
    /// <param name="path">The route template under test.</param>
    [Theory]
    [MemberData(nameof(BackupScopedEndpoints))]
    public async Task An_unknown_backup_identifier_answers_not_found_rather_than_failing(string method, string path)
    {
        // 404, never 500: an identifier the caller invented is answered by the handler's typed
        // NotFound, not by an unhandled failure. And it must be the SAME answer as another tenant's
        // row, or the difference between the two is the oracle an enumeration wanted.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, Substitute(path, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A customer reading their own backup is answered with it.</summary>
    [Fact]
    public async Task A_customer_reading_their_own_backup_is_answered_with_it()
    {
        // The inverse control. Without it the theories above pass for a surface that answers 404 to
        // everybody — a broken route, an unmigrated schema, or a seed that never ran.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync($"/api/v1/backups/{world.OwnBackupId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(world.OwnBackupId, body.RootElement.GetProperty("id").GetGuid());
        // The panel serialises every enum as a camelCase STRING (Host JsonSerializationExtensions),
        // so the status arrives on the wire as a name and not as an ordinal. Deriving the expected
        // name from the member rather than typing "completed" keeps a rename of the member visible
        // here instead of silently comparing against a literal nothing produces any more.
        Assert.Equal(
            JsonNamingPolicy.CamelCase.ConvertName(nameof(BackupStatus.Completed)),
            body.RootElement.GetProperty("status").GetString());
    }

    /// <summary>Listing backups shows a customer only their own.</summary>
    [Fact]
    public async Task Listing_backups_shows_a_customer_only_their_own()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/backups");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = body.RootElement.EnumerateArray().Select(row =>
        {
            return row.GetProperty("id").GetGuid();
        }).ToList();

        Assert.Equal([world.OwnBackupId], ids);
    }

    /// <summary>An administrator is listed both tenants backups.</summary>
    [Fact]
    public async Task An_administrator_is_listed_both_tenants_backups()
    {
        // The inverse control on the tenant axis, over real HTTP: the same two rows, a principal the
        // filter does not narrow, and both required back. A filter mutated to hide everything would
        // leave the customer's listing above correct-looking and empty.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/backups");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetArrayLength());
    }

    /// <summary>Creating a backup for another tenants account is answered not found.</summary>
    [Fact]
    public async Task Creating_a_backup_for_another_tenants_account_is_answered_not_found()
    {
        // The create route is scoped by the account in its BODY rather than by a route identifier,
        // so the theories above cannot reach it; without this test the one endpoint that starts work
        // on the host would have no tenancy coverage at all. It is refused before the agent is
        // reached, which is why this can be asserted without a running agent.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.PostAsJsonAsync(
            "/api/v1/backups", new { accountId = world.StrangerAccountId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        Assert.Equal(2, await backups.Backups.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>Every backup scoped route on the controller is covered by the idor fixture.</summary>
    /// <remarks>
    /// The fixtures above are hand-written lists, and a hand-written list of routes goes stale the
    /// first time somebody adds a route. This reads the routes off <see cref="BackupsController"/>
    /// itself, so a new backup-scoped endpoint — restore, above all — fails HERE, naming itself,
    /// rather than quietly enjoying no IDOR coverage.
    /// </remarks>
    [Fact]
    public void Every_backup_scoped_route_on_the_controller_is_covered_by_the_idor_fixture()
    {
        var declared = ControllerRoutes.Declared<BackupsController>();
        Assert.NotEmpty(declared);

        var scopedInFixture = RouteStrings(BackupScopedEndpoints());
        var allInFixture = RouteStrings(AllBackupEndpoints());

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
            "These BackupsController routes are absent from AllBackupEndpoints(): "
            + string.Join(", ", missingFromAll));
        Assert.True(
            missingFromScoped.Count == 0,
            "These backup-scoped routes are absent from BackupScopedEndpoints(), so nothing proves "
            + "they answer 404 rather than 403 for another tenant: " + string.Join(", ", missingFromScoped));
    }

    /// <summary>Flattens a theory's rows into "METHOD /path" strings.</summary>
    /// <param name="rows">One of the fixtures above.</param>
    /// <returns>The routes it names.</returns>
    private static HashSet<string> RouteStrings(TheoryData<string, string> rows)
    {
        return rows
            .Select(row =>
            {
                return $"{row[0]} {row[1]}";
            })
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Substitutes a backup id into a route template.</summary>
    /// <param name="path">The route template.</param>
    /// <param name="backupId">The identifier to place in it.</param>
    /// <returns>The absolute path to request.</returns>
    private static string Substitute(string path, Guid backupId)
    {
        return path.Replace("{id}", backupId.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Issues one request, giving the create route a body its model binder accepts.</summary>
    /// <remarks>
    /// The body must be VALID. A route whose body fails validation answers 400, which would make an
    /// authorization theory pass without the request reaching the handler under test.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            // One body serves both POST routes: create reads the account, restore reads the typed
            // confirmation, and each ignores the other's field. The confirmation is a well-formed
            // user name on purpose — a malformed one would be refused by the validator with 400 and
            // the theory would then be measuring the validator rather than the tenancy refusal it
            // is about. It matches no account here, so the restore is still refused; it is refused
            // for being another tenant's backup, which is the answer under test.
            request.Content = JsonContent.Create(
                new { accountId = Guid.NewGuid(), confirmAccountUsername = "nobody" });
        }

        return await client.SendAsync(request);
    }

    /// <summary>Applies every module's migrations this class reads, as the installer does.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The task that migrates them.</returns>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<BackupsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds two accounts, their logins, and one completed backup each.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The identifiers the tests address.</returns>
    private static async Task<SeededWorld> SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
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

        // Written through a context resolved OUTSIDE a request, so the seed itself does no tenant
        // separating and the filter under test is the only thing that can.
        var ownBackup = CompletedBackup(own.Id, now);
        var strangerBackup = CompletedBackup(stranger.Id, now);
        backups.Backups.AddRange(ownBackup, strangerBackup);
        await backups.SaveChangesAsync();

        return new SeededWorld(ownBackup.Id, strangerBackup.Id, stranger.Id);
    }

    /// <summary>Builds one finished backup row for <paramref name="accountId"/>.</summary>
    /// <param name="accountId">The owning account.</param>
    /// <param name="now">The instant the run began and ended, from the panel's clock.</param>
    /// <returns>A row in the state a finished run leaves behind.</returns>
    private static Backup CompletedBackup(Guid accountId, DateTimeOffset now)
    {
        var backup = new Backup(Guid.NewGuid(), accountId, destinationId: null, BackupKind.Manual, now);
        backup.Completed(4096, new string('a', 64), 1, now);
        return backup;
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    /// <returns>A client authenticated as that user.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Boots the host against this class's PostgreSQL.</summary>
    /// <returns>The booted factory.</returns>
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

    /// <summary>The identifiers a seeded world hands to the tests.</summary>
    /// <param name="OwnBackupId">The backup belonging to the signed-in customer.</param>
    /// <param name="StrangerBackupId">The backup belonging to the other tenant.</param>
    /// <param name="StrangerAccountId">The other tenant's account, which create must refuse.</param>
    private sealed record SeededWorld(Guid OwnBackupId, Guid StrangerBackupId, Guid StrangerAccountId);
}
