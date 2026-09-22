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
using Maran.Modules.Licensing.Controllers;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The licence-status surface over real HTTP: who may read the installation's licence status, and
/// what a refused caller learns from the refusal.
/// </summary>
/// <remarks>
/// <para>
/// <b>It answers 403 and deliberately not 404</b> — the same contrast
/// <c>DatabaseGrantsAuthorizationTests</c> draws for its own surface, and for the identical reason:
/// there is no identifier here to use as an oracle. One installation has exactly one licence status,
/// and it plainly exists (even <c>Absent</c> is an answer), so the role policy IS the authorisation
/// and there is no query filter behind it to 404 in front of.
/// </para>
/// <para>
/// <b>Every refusing assertion has an inverse control</b>: a policy weakened to admit any signed-in
/// caller would pass a test that only hands it a customer to refuse, so the administrator path is
/// asserted to succeed with 200 and the licence status body in the same breath.
/// </para>
/// <para>
/// No agent double is needed here — unlike the grant-repair surface, this one reads nothing from the
/// host, only the module's own (always-absent, in this build) <c>ILicenceRawTextSource</c>.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class LicensingAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every route under <c>/api/v1/licence-status</c>.</summary>
    /// <remarks>
    /// Completeness is asserted by
    /// <see cref="Every_licence_status_route_is_covered_by_the_role_fixture"/>, the same
    /// self-checking shape <c>DatabaseGrantsAuthorizationTests</c> uses.
    /// </remarks>
    public static TheoryData<string, string> LicenceStatusEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/licence-status" },
            { "POST", "/api/v1/licence-status/install" },
        };
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public LicensingAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every licence-status route.</summary>
    [Theory]
    [MemberData(nameof(LicenceStatusEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_licence_status_route(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed in customer is refused with 403, never 404, by every licence-status route.</summary>
    [Theory]
    [MemberData(nameof(LicenceStatusEndpoints))]
    public async Task A_signed_in_customer_is_refused_with_forbidden_by_every_licence_status_route(
        string method,
        string path)
    {
        // 403, and deliberately not 404: there is no identifier here and no tenant, because one
        // installation has one licence status and it is an operator's fact, not a customer's.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An administrator reading the status is answered 200 with the three-state report.</summary>
    [Fact]
    public async Task An_administrator_reading_the_status_is_answered_with_the_report()
    {
        // The inverse control for the role gate: a policy that refused every caller, administrators
        // included, would pass every theory above while leaving the surface dead. With nothing
        // installed on this freshly booted host, the honest answer is "Absent" — the ordinary
        // first-run state, reported with 200 OK and not as any kind of error.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/licence-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Absent", body.RootElement.GetProperty("state").GetString());

        // The response never carries a signature or a fingerprint — nothing on this build's Licence
        // or LicenceRefusalReason types has either shape to leak, so this checks the literal wire
        // shape rather than a source type that could be extended later without this test noticing.
        Assert.False(body.RootElement.TryGetProperty("signature", out _));
        Assert.False(body.RootElement.TryGetProperty("fingerprint", out _));
    }

    /// <summary>
    /// An administrator installing a licence reaches the handler — never refused by the role gate —
    /// while a signed-in customer is refused before the handler runs at all. Both directions of the
    /// mandatory "admin-only policy weakened" mutant this slice's proof names: a policy narrowed to
    /// refuse everyone would still pass the customer theory above, and a policy widened to admit any
    /// signed-in caller would still pass this test's customer half alone — only checking BOTH closes
    /// the mutant.
    /// </summary>
    [Fact]
    public async Task An_administrator_installing_a_licence_reaches_the_handler_while_a_customer_is_refused_at_the_gate()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);

        using var adminClient = await SignInAsync(factory, "admin");
        using var customerClient = await SignInAsync(factory, "customer");

        // Deliberately not a genuine signed licence: this test project holds no Innovayse private
        // key (see LicensingStartupTests's own remarks on what this suite cannot observe). Malformed
        // text is enough to prove the ADMINISTRATOR reached the handler at all — a 400 from
        // InstallLicenceCommandHandler's own verification, not a 401/403 from the authorization gate.
        var adminResponse = await adminClient.PostAsJsonAsync(
            "/api/v1/licence-status/install", new { RawLicenceText = "this is not a licence envelope" });
        var customerResponse = await customerClient.PostAsJsonAsync(
            "/api/v1/licence-status/install", new { RawLicenceText = "this is not a licence envelope" });

        Assert.Equal(HttpStatusCode.BadRequest, adminResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, customerResponse.StatusCode);
    }

    /// <summary>An administrator's read is written to the audit journal.</summary>
    /// <remarks>
    /// This is the proof that <c>Services.LicensingAuditJournal.RecordReadAsync</c> actually runs
    /// from the live handler rather than only in its own unit tests: a mutant that deleted the
    /// journal call from <c>GetLicenceStatusQueryHandler.HandleAsync</c> would still pass every
    /// other assertion in this class, because none of them look at the journal.
    /// </remarks>
    [Fact]
    public async Task An_administrators_read_is_written_to_the_audit_journal()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/licence-status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var recorded = await identity.AuditEvents
            .Where(entry => entry.Action == AuditActions.LicenceStatusRead)
            .ToListAsync();

        Assert.Single(recorded);
        Assert.True(recorded[0].Succeeded);
        Assert.Equal("status=Absent", recorded[0].Subject);
    }

    /// <summary>Every licence-status route on the controller is covered by the role fixture.</summary>
    [Fact]
    public void Every_licence_status_route_is_covered_by_the_role_fixture()
    {
        var declared = ControllerRoutes.Declared<LicensingController>();
        Assert.NotEmpty(declared);

        var inFixture = LicenceStatusEndpoints()
            .Select(row =>
            {
                return $"{row[0]} {row[1]}";
            })
            .ToHashSet(StringComparer.Ordinal);

        var missing = declared.Where(route =>
        {
            return !inFixture.Contains(route);
        }).ToList();

        Assert.True(
            missing.Count == 0,
            "These LicensingController routes are absent from LicenceStatusEndpoints(), so nothing "
            + "proves they are closed to a customer: " + string.Join(", ", missing));
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

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }

    /// <summary>Applies the migrations the surfaces under test need.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<AccountsDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds one account with an administrator and a customer signed into it.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var planId = Guid.NewGuid();
        accounts.Plans.Add(new Plan(planId, "PlanStarterName", 5_120, 5, 2, 3, 5, 5, 3));
        var own = new Account(Guid.NewGuid(), "own", "own.example.com", planId, now);
        accounts.Accounts.Add(own);
        await accounts.SaveChangesAsync();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        var customer = new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now);
        customer.AssignAccount(own.Id);
        identity.Users.Add(customer);
        await identity.SaveChangesAsync();
    }

    /// <summary>Signs in over the real endpoint and returns a client carrying the access token.</summary>
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
}
