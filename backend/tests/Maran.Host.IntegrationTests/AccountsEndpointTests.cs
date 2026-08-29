using System.Net;
using Maran.Modules.Accounts.Domain;
using Maran.Modules.Accounts.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Testcontainers.PostgreSql;

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
/// Four real, independent production defects surfaced while writing this class (reported here and
/// in accounts-tests-report.md, not fixed — rules/testing.md "Do NOT modify production code to make
/// a test pass"):
/// <list type="number">
/// <item>
/// <c>AccountsController</c> is decorated <c>[Route("accounts")]</c>. A non-<c>AllowMultiple</c>
/// attribute declared on a derived type hides the same attribute declared on its base — so
/// <see cref="Sdk.Controllers.BaseApiController"/>'s <c>[Route("api/v1/[controller]")]</c> never
/// applies here, and the account endpoints are actually reachable at <c>/accounts</c>, not the
/// <c>/api/v1/accounts</c> the controller's own XML docs, the spec, and every other controller in
/// the codebase assume. Confirmed empirically: <c>GET /api/v1/accounts</c> against the real host
/// returns 404; <c>GET /accounts</c> is routed.
/// </item>
/// <item>
/// Nothing registers <see cref="SharedKernel.Interfaces.ICurrentUser"/> in the Host's DI container
/// (<see cref="Sdk.Controllers.BaseApiController"/>'s own constructor doc comment says as much: "No
/// implementation is registered until authentication ships"). Confirmed empirically: every request
/// that reaches <c>AccountsController</c> — the module's only controller — throws
/// <c>InvalidOperationException</c> ("Unable to resolve service for type
/// '…ICurrentUser' while attempting to activate 'AccountsController'") while ASP.NET Core
/// activates it, which <c>ExceptionMiddleware</c> turns into a 500 <c>HostUnexpectedError</c>.
/// The Accounts HTTP surface cannot currently serve a single request.
/// </item>
/// <item>
/// No EF Core migration exists for the Accounts module (<c>Persistence/Migrations/</c> holds only a
/// <c>.gitkeep</c>) and nothing in <c>Maran.Host</c>'s startup path applies one, so a real
/// deployment against a fresh PostgreSQL never gets an <c>accounts."Accounts"</c> table at all.
/// <see cref="Accounts_schema_is_created_and_an_account_round_trips_through_postgres"/> creates the
/// schema itself, purely as test setup (via <c>IRelationalDatabaseCreator</c> directly, bypassing
/// <c>EnsureCreatedAsync</c>'s no-op "database already exists" check against Testcontainers'
/// pre-provisioned database) — production code is left untouched.
/// </item>
/// <item>
/// <c>Maran.Host.Extensions.MessagingExtensions.AddPanelMessaging</c> sets
/// <c>options.AutoBuildMessageStorageOnStartup = AutoCreate.None</c> whenever a connection string
/// is configured, by explicit design ("Schema changes are applied deliberately by the installer and
/// the update command … never as a side effect of a process start"). That is correct for
/// production, but it also means <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// can never boot <c>Program</c> against a genuinely fresh database — Wolverine's own
/// <c>wolverine.wolverine_incoming_envelopes</c> table is never provisioned, and host startup fails
/// with "The Wolverine message storage for database 'default' is missing". This reproduces for the
/// pre-existing, unrelated <c>HostBootTests.Host_boots_with_postgres_and_serves_health</c> too — it
/// is not specific to Accounts or to anything added in this pass — which means, in this
/// environment, no <c>WebApplicationFactory&lt;Program&gt;</c>-based test can currently exercise a
/// live HTTP endpoint end-to-end. Defects 1 and 2 above were confirmed by one successful earlier
/// boot in this same session (captured in accounts-tests-report.md with full log output) before this
/// fourth issue was isolated; the two tests that depended on a live HTTP round trip are
/// <c>Skip</c>-documented below rather than left non-deterministically red, per rules/testing.md
/// "Determinism" and "never retry-loop it".
/// </item>
/// </list>
/// </remarks>
public sealed class AccountsEndpointTests : IAsyncLifetime
{
    /// <summary>The disposable PostgreSQL instance shared by every test in this class.</summary>
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.StartAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return _pg.DisposeAsync().AsTask();
    }

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
            setupContext.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3));
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
    /// DEFECT 1 (see class remarks): the documented, mandated <c>GET /api/v1/accounts</c> does not
    /// resolve to the Accounts controller at all.
    /// </summary>
    [Fact(Skip =
        "Blocked by defect 4 (see class remarks): WebApplicationFactory<Program> cannot currently " +
        "boot against a fresh PostgreSQL because Wolverine's own message storage is never " +
        "auto-provisioned (AutoBuildMessageStorageOnStartup = AutoCreate.None by design), so no live " +
        "HTTP round trip is possible in this environment right now — reproduces for the pre-existing " +
        "HostBootTests too. Defect 1 itself (the route mismatch) was confirmed empirically earlier in " +
        "this session: GET /api/v1/accounts returned 404 Not Found. See accounts-tests-report.md.")]
    public async Task Get_on_the_documented_api_v1_accounts_path_currently_404s()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // Testing, not Development: appsettings.Development.json points at a developer's own
            // PostgreSQL, and inheriting it made these tests pass locally by accident while
            // connecting to the wrong database — and fail in CI, where nothing listens there.
            b.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                b.UseSetting(setting.Key, setting.Value);
            }
            b.UseSetting("Security:EncryptionKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/accounts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// DEFECT 2 (see class remarks): the account endpoints are actually reachable at
    /// <c>/accounts</c>, but every request there 500s for a missing <c>ICurrentUser</c> registration.
    /// </summary>
    [Fact(Skip =
        "Blocked by defect 4 (see class remarks): WebApplicationFactory<Program> cannot currently " +
        "boot against a fresh PostgreSQL because Wolverine's own message storage is never " +
        "auto-provisioned (AutoBuildMessageStorageOnStartup = AutoCreate.None by design), so no live " +
        "HTTP round trip is possible in this environment right now — reproduces for the pre-existing " +
        "HostBootTests too. Defect 2 itself (missing ICurrentUser registration) was confirmed " +
        "empirically earlier in this session: GET /accounts returned 500 with an unhandled " +
        "InvalidOperationException resolving ICurrentUser for AccountsController. See " +
        "accounts-tests-report.md.")]
    public async Task Get_on_the_actual_accounts_path_currently_500s_for_missing_current_user_registration()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // Testing, not Development: appsettings.Development.json points at a developer's own
            // PostgreSQL, and inheriting it made these tests pass locally by accident while
            // connecting to the wrong database — and fail in CI, where nothing listens there.
            b.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                b.UseSetting(setting.Key, setting.Value);
            }
            b.UseSetting("Security:EncryptionKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/accounts");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
