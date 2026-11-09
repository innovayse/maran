using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Seeders;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Backups.Seeders;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Ssl.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>GET /api/v1/dashboard/first-run</c> over real HTTP: who may read the first-run checklist, that
/// the plans and the backup destination a fresh installation seeds do not tick any step, and that
/// what the administrator then makes does.
/// </summary>
/// <remarks>
/// <para>
/// The checklist is server-wide, not a tenant's row, so the isolation question is "a customer is
/// refused", not "another account's row answers 404". The route is a read and writes nothing, so
/// there is no audit entry to assert.
/// </para>
/// <para>
/// <b>Every refusing assertion has an inverse control.</b> The administrator path is asserted to carry
/// the checklist in the same file as the customer path is asserted not to, and the seeded-only server
/// (every step open) is followed by one where the administrator's own plan and destination tick
/// exactly their two steps, so an endpoint that always answered "open" would not pass.
/// </para>
/// <para>
/// UNOBSERVED HERE: the accounts, sites and certificates counts are asserted per module in the
/// module test projects; this file observes the route, the policy, the seeds and the two steps the
/// seeds could have ticked.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class FirstRunEndpointTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public FirstRunEndpointTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused outright.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/dashboard/first-run");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed-in customer is refused with 403 and is told nothing about the server.</summary>
    [Fact]
    public async Task A_signed_in_customer_is_refused()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/dashboard/first-run");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// On a server holding only what the installation seeds — the three standard plans and the local
    /// backup destination — no step is done and the checklist is not complete.
    /// </summary>
    [Fact]
    public async Task A_freshly_seeded_server_has_every_step_open()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        await SeedInstallationDefaultsAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/dashboard/first-run");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.False(body.GetProperty("complete").GetBoolean());
        Assert.Equal(
            ["plan", "account", "site", "backupDestination", "certificate"],
            Steps(body).Select(step =>
            {
                return step.Key;
            }).ToArray());
        Assert.All(Steps(body), step =>
        {
            Assert.False(step.Done);
        });
    }

    /// <summary>
    /// A plan and a backup destination the administrator made tick exactly their own two steps,
    /// beside the seeded ones, which tick nothing.
    /// </summary>
    [Fact]
    public async Task What_the_administrator_made_ticks_only_its_own_steps()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        await SeedInstallationDefaultsAsync(factory);
        await AddAdministratorsPlanAndDestinationAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var body = await ReadAsync(await client.GetAsync("/api/v1/dashboard/first-run"));

        Assert.Equal(
            new[]
            {
                ("plan", true),
                ("account", false),
                ("site", false),
                ("backupDestination", true),
                ("certificate", false),
            },
            Steps(body).Select(step =>
            {
                return (step.Key, step.Done);
            }).ToArray());
        Assert.False(body.GetProperty("complete").GetBoolean());
    }

    /// <summary>Boots the panel against this test's own database.</summary>
    /// <returns>The booted host factory.</returns>
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

    /// <summary>Applies the migrations of the contexts the checklist reads through.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        Type[] contexts =
        [
            typeof(IdentityDbContext),
            typeof(AccountsDbContext),
            typeof(SitesDbContext),
            typeof(BackupsDbContext),
            typeof(SslDbContext),
        ];

        using var scope = factory.Services.CreateScope();
        foreach (var context in contexts)
        {
            var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(context);
            await dbContext.Database.MigrateAsync();
        }
    }

    /// <summary>Writes the rows a fresh installation seeds at start-up: the standard plans and the local destination.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedInstallationDefaultsAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        await new PlanSeeder(accounts).SeedAsync(CancellationToken.None);
        await scope.ServiceProvider.GetRequiredService<DefaultBackupDestinationSeeder>()
            .SeedAsync(CancellationToken.None);
    }

    /// <summary>Adds a plan of the administrator's own and a second backup destination.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task AddAdministratorsPlanAndDestinationAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        accounts.Plans.Add(new Plan(Guid.NewGuid(), "PlanAgencyName", 5_120, 5, 2, 3, 5, 5, 3));
        await accounts.SaveChangesAsync();

        backups.BackupDestinations.Add(new BackupDestination(
            Guid.NewGuid(), "Offsite", BackupDestinationKind.S3, "bucket/path", isDefault: false, now));
        await backups.SaveChangesAsync();
    }

    /// <summary>Seeds an administrator and a customer signed into one account.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedUsersAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        identity.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), customer.Id, Guid.NewGuid(), now));
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();
    }

    /// <summary>Signs a seeded user in and returns a client carrying their token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">Which seeded user to sign in.</param>
    /// <returns>An authenticated client.</returns>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Reads an answer's body as a detached JSON element.</summary>
    /// <param name="response">The response.</param>
    /// <returns>The body's root element.</returns>
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    /// <summary>Reads the checklist's steps, requiring each to have been read.</summary>
    /// <param name="body">The answer's body.</param>
    /// <returns>Each step's key and whether it is done.</returns>
    private static List<(string Key, bool Done)> Steps(JsonElement body)
    {
        return body.GetProperty("steps").EnumerateArray()
            .Select(step =>
            {
                var done = step.GetProperty("done");
                Assert.Equal("loaded", done.GetProperty("state").GetString());
                return (step.GetProperty("key").GetString()!, done.GetProperty("data").GetBoolean());
            })
            .ToList();
    }
}
