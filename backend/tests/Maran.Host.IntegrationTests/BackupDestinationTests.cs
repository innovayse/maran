using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Seeders;
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
/// The backup destinations surface over real HTTP against real PostgreSQL — who may reach it, what a
/// refused remote destination answers, and the constraint only PostgreSQL can enforce.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gate is a ROLE, not ownership.</b> Where the server keeps its archives is a decision about
/// the machine and its disks, so an anonymous caller is answered 401 and a signed-in customer 403 —
/// the same answers <c>BackupSchedulesController</c> gives. 404 would be the TENANT answer, and a
/// destination names no customer's data for it to be about.
/// </para>
/// <para>
/// <b>The "at most one default" rule is a partial unique index</b>, which only PostgreSQL enforces
/// and only PostgreSQL can be asked about. Two default destinations would mean two answers to "where
/// does a backup that names none go", and the one a given query returned would be arbitrary.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class BackupDestinationTests : IAsyncLifetime
{
    /// <summary>The password every seeded login is given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>A base64 key long enough for the host's startup validation of both key settings.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The database this test class owns on the shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public BackupDestinationTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Every route the backup destinations surface declares.</summary>
    public static TheoryData<string, string> DestinationEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/backup-destinations" },
            { "POST", "/api/v1/backup-destinations" },
        };
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>The task that creates this class's database.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases the fixture after the tests run.</summary>
    /// <returns>The completed task; the shared server owns the database's life.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>An anonymous caller is refused every destinations route.</summary>
    /// <param name="method">The HTTP method of the route.</param>
    /// <param name="path">The path of the route.</param>
    [Theory]
    [MemberData(nameof(DestinationEndpoints))]
    public async Task An_anonymous_caller_is_refused_every_destinations_route(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);

        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed-in customer is refused every destinations route with 403, never 404.</summary>
    /// <param name="method">The HTTP method of the route.</param>
    /// <param name="path">The path of the route.</param>
    [Theory]
    [MemberData(nameof(DestinationEndpoints))]
    public async Task A_customer_is_refused_every_destinations_route(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);

        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An administrator reads back the destination the startup reconciliation wrote.</summary>
    /// <remarks>
    /// <para>
    /// The inverse control for both refusals above: a surface mutated to refuse everybody passes
    /// every 401 and 403 row and fails here. It also proves the reconciliation writes a row
    /// PostgreSQL accepts, filtered unique index and all.
    /// </para>
    /// <para>
    /// <b>The path comes back null, and that is the answer being pinned.</b> There is no agent
    /// behind this host, so the panel could not ask where backups are written — and the directory is
    /// the agent's to state. A response carrying <c>/var/backups/maran</c> here would mean the panel
    /// had produced a path from somewhere other than the agent, which is exactly the defect this
    /// surface was changed to close; the case where the agent does answer is measured against a
    /// scripted handshake in <c>Maran.Modules.Backups.Tests</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_administrator_reads_back_the_reconciled_default_destination()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        await ReconcileAsync(factory);

        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/backup-destinations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var only = Assert.Single(body.RootElement.EnumerateArray().ToList());
        Assert.True(only.GetProperty("isDefault").GetBoolean());
        // camelCase, which is what the panel's JSON options do to every enum member on this wire.
        Assert.Equal("local", only.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, only.GetProperty("path").ValueKind);
    }

    /// <summary>An administrator asking for S3-compatible storage is refused by name.</summary>
    /// <remarks>
    /// The refusal is the product's honest answer to an open decision, and the machine-stable code is
    /// what a settings screen branches on to say so in the operator's own language. A 404 on an
    /// endpoint that was never written would leave the screen with nothing to say.
    /// </remarks>
    [Fact]
    public async Task An_administrator_asking_for_s3_storage_is_refused_by_name()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        await ReconcileAsync(factory);

        using var client = await SignInAsync(factory, "admin");

        var response = await client.PostAsJsonAsync(
            "/api/v1/backup-destinations", new { name = "Off-site", kind = "S3" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("BackupDestinationRemoteUnsupported", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>A second default destination is refused by the database, not by hope.</summary>
    [Fact]
    public async Task A_second_default_destination_is_refused_by_the_database()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        await ReconcileAsync(factory);

        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        backups.BackupDestinations.Add(new BackupDestination(
            Guid.NewGuid(), "Second", BackupDestinationKind.Local, "/mnt/backup", isDefault: true, now));

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await backups.SaveChangesAsync();
        });
    }

    /// <summary>A backup row still pointing at no destination survives the new foreign key.</summary>
    /// <remarks>
    /// The expand-then-contract half, measured rather than argued: every row written before the
    /// destinations table existed carries null, the column was deliberately NOT narrowed, and the
    /// foreign key added beside it must therefore accept those rows unchanged. If it did not, the
    /// previous release would stop running against this schema and the installer's rollback promise
    /// would be unkeepable.
    /// </remarks>
    [Fact]
    public async Task A_backup_row_naming_no_destination_is_still_accepted()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);
        await ReconcileAsync(factory);

        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        backups.Backups.Add(new Backup(Guid.NewGuid(), world.AccountId, null, BackupKind.Manual, now));
        await backups.SaveChangesAsync();

        Assert.Equal(1, await backups.Backups.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>Sends a bodyless request of the given method to the given path.</summary>
    /// <param name="client">The client to send it on.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path.</param>
    /// <returns>The response.</returns>
    /// <remarks>
    /// A POST is given a well-formed body, so a refusal is the authorization filter's and not the
    /// input formatter's — a 400 for a missing body would look like a pass on a route that never
    /// checked the caller's role.
    /// </remarks>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (method is "POST")
        {
            request.Content = JsonContent.Create(new { name = "Off-site", kind = "Local" });
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

    /// <summary>Runs the startup reconciliation by hand, after the schema exists.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The task that writes the default destination.</returns>
    /// <remarks>
    /// The hosted service runs at boot, which in this fixture is BEFORE the migrations these tests
    /// apply — so it logs that it could not reconcile and swallows, exactly as it is written to. The
    /// same registered seeder is resolved here, so what is measured is the production type against
    /// the production schema rather than a re-implementation of it.
    /// </remarks>
    private static async Task ReconcileAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DefaultBackupDestinationSeeder>();
        await seeder.SeedAsync(CancellationToken.None);
    }

    /// <summary>Seeds one account and the two logins the tests sign in as.</summary>
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
        var account = new Account(Guid.NewGuid(), "own", "own.example.com", planId, now);
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
    /// <param name="AccountId">The one account on the seeded host.</param>
    private sealed record SeededWorld(Guid AccountId);
}
