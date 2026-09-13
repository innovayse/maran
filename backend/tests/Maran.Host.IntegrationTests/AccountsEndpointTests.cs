using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Exercises the Accounts module's <c>accounts</c> PostgreSQL schema and its HTTP surface over a
/// disposable PostgreSQL (rules/testing.md "Definition of Done" — integration test of the real
/// surface). Lives here, in <c>Maran.Host.IntegrationTests</c>, rather than a new per-module
/// integration project: the canonical layout (rules/csharp.md "Canonical backend layout") names
/// exactly one project for this kind of test — "HTTP/DB surface, Testcontainers" — and a
/// module-specific integration project is not a shape the map defines. One PostgreSQL container is
/// shared by the whole class (rules/testing.md "Keep integration tests to one PostgreSQL container
/// per test class at most").
/// </summary>
/// <remarks>
/// This class was written against four real production defects and kept them documented rather
/// than worked around: the controller's route had drifted from <c>/api/v1/accounts</c> to
/// <c>/accounts</c>; nothing registered <c>ICurrentUser</c>, so every request that reached the
/// controller 500d during activation; the Accounts module had no EF Core migration; and
/// <c>WebApplicationFactory&lt;Program&gt;</c> could not boot against a fresh PostgreSQL because
/// Wolverine's message storage is never auto-provisioned. All four are now closed — the route is
/// restored, <c>HttpContextCurrentUser</c> is registered, <c>InitialAccountsSchema</c> exists, and
/// the host boots here and in <c>SetupEndpointTests</c> — so the two tests that were
/// <c>Skip</c>-documented against the broken behaviour now assert the correct behaviour instead.
/// They are kept because each one still fails if its defect returns: a route drift shows up as 404
/// where 401 is expected, and a missing <c>ICurrentUser</c> as 500 where 200 is.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AccountsEndpointTests : IAsyncLifetime
{
    private const string EncryptionKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string SetupToken = "a-one-time-token";
    private const string AdminPassword = "correct horse battery staple";

    /// <summary>The disposable PostgreSQL instance shared by every test in this class.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountsEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>Accounts schema is created and an account round trips through postgres.</summary>
    [Fact]
    public async Task Accounts_schema_is_created_and_an_account_round_trips_through_postgres()
    {
        // Constructed directly against the module's own DbContext/connection string — deliberately
        // bypassing WebApplicationFactory<Program> and its Wolverine-dependent boot path (defect 4
        // above), so this test still exercises the real thing it is named for: the accounts schema
        // and entity mapping against a real PostgreSQL, independent of the Host's messaging setup.
        var options = new DbContextOptionsBuilder<AccountsDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .Options;

        var planId = Guid.NewGuid();
        await using (var setupContext = new AccountsDbContext(options))
        {
            var databaseCreator = setupContext.GetService<IRelationalDatabaseCreator>();
            await databaseCreator.CreateTablesAsync();

            // Account.PlanId carries a real foreign key to Plans (rules/csharp.md "Database
            // naming" — FK_Accounts_Plans_PlanId), so a plan row must exist first.
            setupContext.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5));
            await setupContext.SaveChangesAsync();
        }

        var account = new Account(
            Guid.NewGuid(),
            "roundtrip",
            "roundtrip.example.com",
            planId,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await using (var writeContext = new AccountsDbContext(options))
        {
            writeContext.Accounts.Add(account);
            await writeContext.SaveChangesAsync();
        }

        // A second, independent DbContext instance against the same container — proves the row is
        // durably persisted in PostgreSQL, not just tracked by the first context's change tracker.
        await using var readContext = new AccountsDbContext(options);
        var reloaded = await readContext.Accounts.FindAsync(account.Id);

        Assert.NotNull(reloaded);
        Assert.Equal("roundtrip", reloaded!.Name);
        Assert.Equal("roundtrip.example.com", reloaded.PrimaryDomain);
    }

    /// <summary>
    /// The Accounts surface answers on the documented path, and answers closed. A 404 here would
    /// mean the controller's route had drifted from the one the spec, its own XML docs and the SPA
    /// all use — the defect this class was written to catch.
    /// </summary>
    [Fact]
    public async Task The_documented_accounts_path_exists_and_refuses_an_anonymous_caller()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// An authenticated administrator gets a list, not a 500. This is the end-to-end proof that
    /// <c>ICurrentUser</c> resolves for a real request: the controller cannot be activated without
    /// it, so a missing registration surfaces here as an unhandled activation failure.
    /// </summary>
    [Fact]
    public async Task An_authenticated_administrator_can_list_accounts()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();
        var token = await SignInAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/accounts");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Boots the host against this class's PostgreSQL, as the setup tests do.</summary>
    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // Testing, not Development: appsettings.Development.json points at a developer's own
            // PostgreSQL, and inheriting it made these tests pass locally by accident while
            // connecting to the wrong database — and fail in CI, where nothing listens there.
            b.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                b.UseSetting(setting.Key, setting.Value);
            }

            b.UseSetting("Security:EncryptionKey", EncryptionKey);
            b.UseSetting("Jwt:SigningKey", EncryptionKey);

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                b.UseSetting(setting.Key, setting.Value);
            }
            b.UseSetting("Setup:Token", SetupToken);
        });
    }

    /// <summary>Applies both modules' migrations, the way the installer does before first boot.</summary>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Creates the first administrator and signs in, returning the access token.</summary>
    private static async Task<string> SignInAsync(HttpClient client)
    {
        await client.PostAsJsonAsync(
            "/api/v1/setup",
            new { Token = SetupToken, Username = "admin", Email = "admin@example.com", Password = AdminPassword });

        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { Username = "admin", Password = AdminPassword });

        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
    }
}
