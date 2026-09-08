using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Backups.Domain.Entities;
using Maran.Modules.Backups.Domain.Enums;
using Maran.Modules.Backups.Jobs;
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
/// The backup schedule surface over real HTTP against real PostgreSQL — who may reach it — and the
/// one query that cannot be measured anywhere else: the selection the nightly sweep issues.
/// </summary>
/// <remarks>
/// <para>
/// <b>The gate is a ROLE, not ownership.</b> When the server backs an account up and how many copies
/// it keeps is a decision about the machine and its disk, so an anonymous caller is answered 401 and
/// a signed-in customer 403 — the same answers <c>FirewallRulesController</c> and
/// <c>SmtpSettingsController</c> give, which is this tree's admin-gating idiom. 404 would be the
/// wrong answer here: it is the TENANT answer, and using it would say a schedule "does not exist" to
/// a caller who is simply not an administrator.
/// </para>
/// <para>
/// <b>Why the sweep is exercised here and not in the module's unit tests.</b> Those run on the
/// in-memory provider, which does not translate a query — so a selection that PostgreSQL would
/// refuse, or a filtered unique index that PostgreSQL would enforce, is invisible to them. Driven
/// against the real server the rest of this assembly boots, the reads and the constraint are the
/// ones production issues.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class BackupScheduleTests : IAsyncLifetime
{
    /// <summary>The password every seeded login is given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>A base64 key long enough for the host's startup validation of both key settings.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The database this test class owns on the shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public BackupScheduleTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Every route the backup schedule surface declares.</summary>
    public static TheoryData<string, string> ScheduleEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/backup-schedules" },
            { "PUT", "/api/v1/backup-schedules" },
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

    /// <summary>An anonymous caller is refused every schedule route.</summary>
    /// <param name="method">The HTTP method of the route.</param>
    /// <param name="path">The path of the route.</param>
    [Theory]
    [MemberData(nameof(ScheduleEndpoints))]
    public async Task An_anonymous_caller_is_refused_every_schedule_route(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);

        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed-in customer is refused every schedule route with 403, never 404.</summary>
    /// <param name="method">The HTTP method of the route.</param>
    /// <param name="path">The path of the route.</param>
    /// <remarks>
    /// The 404 assertion is the load-bearing half: 404 is the tenant answer this product gives for a
    /// row somebody else owns, and using it for a role refusal would tell an operator's own screens
    /// that a host-wide setting does not exist.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ScheduleEndpoints))]
    public async Task A_customer_is_refused_every_schedule_route(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);

        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An administrator saves a schedule and reads it back.</summary>
    /// <remarks>
    /// The inverse control for both refusals above: a surface mutated to refuse everybody passes
    /// every 401 and 403 row and fails here.
    /// </remarks>
    [Fact]
    public async Task An_administrator_saves_a_schedule_and_reads_it_back()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);

        using var client = await SignInAsync(factory, "admin");

        var saved = await client.PutAsJsonAsync(
            "/api/v1/backup-schedules",
            new { frequency = "Daily", hourUtc = 3, retainCount = 7, enabled = true });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var read = await client.GetAsync("/api/v1/backup-schedules");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(3, body.RootElement.GetProperty("hourUtc").GetInt32());
        Assert.Equal(7, body.RootElement.GetProperty("retainCount").GetInt32());
        Assert.True(body.RootElement.GetProperty("enabled").GetBoolean());
    }

    /// <summary>A server with no schedule answers 404 rather than an invented default.</summary>
    [Fact]
    public async Task A_server_with_no_schedule_answers_not_found()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);

        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/backup-schedules");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>A second schedule for one account is refused by the database, not by hope.</summary>
    /// <remarks>
    /// The per-account uniqueness is a partial unique index, which only PostgreSQL can enforce and
    /// only PostgreSQL can be asked about. Two schedules for one account would take two backups a
    /// night and neither of them would be wrong.
    /// </remarks>
    [Fact]
    public async Task A_second_schedule_for_one_account_is_refused_by_the_database()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);

        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();

        backups.BackupSchedules.Add(Schedule(world.AccountId));
        await backups.SaveChangesAsync();

        backups.BackupSchedules.Add(Schedule(world.AccountId));

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await backups.SaveChangesAsync();
        });
    }

    /// <summary>The sweep's selection runs against PostgreSQL and picks the enabled schedules only.</summary>
    /// <remarks>
    /// No HTTP, on purpose: the sweep is a message handler with no endpoint, so the host is booted
    /// only to resolve it from the real container — the same registration
    /// <c>BackupScheduleScheduler</c> drives every five minutes. What is measured here is the query,
    /// which the in-memory provider the module's unit tests use does not translate at all.
    /// </remarks>
    [Fact]
    public async Task The_sweeps_selection_runs_against_postgresql_and_picks_the_enabled_schedules_only()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);

        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        // Disabled, and stamped long ago: the only thing keeping it out of the sweep is Enabled.
        var disabled = Schedule(world.AccountId);
        disabled.MarkRunStarted(now.AddYears(-1));
        backups.BackupSchedules.Add(disabled);
        await backups.SaveChangesAsync();

        var handler = scope.ServiceProvider.GetRequiredService<BackupRunHandler>();

        var started = await handler.HandleAsync(new BackupRunRequested(), CancellationToken.None);

        Assert.Equal(0, started);
        Assert.Empty(await backups.Backups.IgnoreQueryFilters().ToListAsync());
    }

    /// <summary>A retention pass's reads and deletes run against PostgreSQL.</summary>
    /// <remarks>
    /// The pass reads with the tenant filter bypassed — it has no principal — and that read is a
    /// statement the provider has to translate, which the in-memory provider the module's unit tests
    /// use does not. UNOBSERVED HERE: the deletion itself. The agent is a separate root process and
    /// is absent from this host, so this case is deliberately below the retained count and asks it
    /// for nothing; what a refused deletion does to the row is measured in
    /// <c>RetentionHandlerTests</c>, over a scripted agent that can refuse.
    /// </remarks>
    [Fact]
    public async Task A_retention_passes_reads_run_against_postgresql()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var world = await SeedAsync(factory);

        using var scope = factory.Services.CreateScope();
        var backups = scope.ServiceProvider.GetRequiredService<BackupsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        for (var day = 0; day < 3; day++)
        {
            var row = new Backup(
                Guid.NewGuid(), world.AccountId, null, BackupKind.Scheduled, now.AddDays(-day));
            row.Completed(1024, new string('a', 64), 1, now.AddDays(-day));
            backups.Backups.Add(row);
        }

        await backups.SaveChangesAsync();

        var handler = scope.ServiceProvider.GetRequiredService<RetentionHandler>();

        var pruned = await handler.HandleAsync(
            new RetentionRequested(world.AccountId, 5), CancellationToken.None);

        Assert.Equal(0, pruned);
        Assert.Equal(3, await backups.Backups.IgnoreQueryFilters().CountAsync());
    }

    /// <summary>Builds a disabled daily schedule for one account.</summary>
    /// <param name="accountId">The account the schedule names.</param>
    /// <returns>The schedule.</returns>
    private static BackupSchedule Schedule(Guid accountId)
    {
        return new BackupSchedule(Guid.NewGuid(), accountId, null, BackupFrequency.Daily, 3, null, 7);
    }

    /// <summary>Sends a bodyless request of the given method to the given path.</summary>
    /// <param name="client">The client to send it on.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path.</param>
    /// <returns>The response.</returns>
    /// <remarks>
    /// A PUT is given an empty JSON object rather than no body at all, so a refusal is the
    /// authorization filter's and not the input formatter's — a 400 for a missing body would look
    /// like a pass on a route that never checked the caller's role.
    /// </remarks>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        if (method is "PUT" or "POST")
        {
            request.Content = JsonContent.Create(new { frequency = "Daily", hourUtc = 3, retainCount = 7 });
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
