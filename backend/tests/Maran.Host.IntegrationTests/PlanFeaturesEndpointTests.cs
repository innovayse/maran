using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Backups.Persistence;
using Maran.Modules.Databases.Persistence;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Plan features over real HTTP against a real PostgreSQL (issue #316): a plan that leaves a feature
/// out is refused at the endpoint of the module that owns it, independently of any menu; an
/// administrator is unaffected; the customer's own account reports the features; and an edit of the
/// plan takes effect on the customer's next request and is journalled.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class PlanFeaturesEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PlanFeaturesEndpointTests(PostgresFixture postgres)
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

    /// <summary>A plan without databases refuses its customer at the databases endpoint and still serves the features it has.</summary>
    [Fact]
    public async Task A_plan_without_databases_refuses_its_customer_at_the_databases_endpoint()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await MigrateListedModulesAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var plan = Plan.Define(
            Guid.NewGuid(), "NoDb", 5_120, 5, 2, 3, 0, 5, 5, AccountFeature.Cron | AccountFeature.Backups);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive", plan);
        await EndpointHarness.SeedCustomerAsync(factory, "olive", accountId);
        using var customer = await EndpointHarness.SignInAsync(factory, "olive");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var refused = await customer.GetAsync("/api/v1/databases");
        var served = await customer.GetAsync("/api/v1/backups");
        var adminRead = await admin.GetAsync("/api/v1/databases");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal("PlanFeatureNotIncluded", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminRead.StatusCode);
    }

    /// <summary>The customer's own account reports the features of its plan as names.</summary>
    [Fact]
    public async Task The_customers_own_account_reports_the_features_of_its_plan_as_names()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var plan = Plan.Define(
            Guid.NewGuid(), "NoDb", 5_120, 5, 2, 3, 0, 5, 5, AccountFeature.FileTransfer | AccountFeature.Backups);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive", plan);
        await EndpointHarness.SeedCustomerAsync(factory, "olive", accountId);
        using var customer = await EndpointHarness.SignInAsync(factory, "olive");

        var response = await customer.GetAsync("/api/v1/accounts/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var features = body.RootElement.GetProperty("plan").GetProperty("features").EnumerateArray()
            .Select(feature =>
            {
                return feature.GetString();
            });
        Assert.Equal(["fileTransfer", "backups"], features);
    }

    /// <summary>An administrator's edit of the plan reaches the customer's next request and is journalled with the sets before and after.</summary>
    [Fact]
    public async Task An_edit_of_the_plan_reaches_the_customers_next_request_and_is_journalled()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await MigrateListedModulesAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var plan = Plan.Define(
            Guid.NewGuid(), "NoDb", 5_120, 5, 2, 3, 0, 5, 5, AccountFeature.Cron | AccountFeature.Backups);
        var (planId, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive", plan);
        await EndpointHarness.SeedCustomerAsync(factory, "olive", accountId);
        using var customer = await EndpointHarness.SignInAsync(factory, "olive");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/databases")).StatusCode);

        var edited = await admin.PutAsJsonAsync($"/api/v1/plans/{planId}", Body("NoDb", ["databases", "cron"]));

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync("/api/v1/databases")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var entry = await identity.AuditEvents.AsNoTracking()
            .SingleAsync(e => e.Action == AccountAuditActions.PlanUpdated);
        Assert.Equal("NoDb [features: Cron, Backups -> Databases, Cron]", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal("admin", entry.ActorUsername);
    }

    /// <summary>A feature name the panel does not know is refused with 400 and the plan keeps its set.</summary>
    [Fact]
    public async Task A_feature_name_the_panel_does_not_know_is_refused_and_the_plan_keeps_its_set()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var plan = Plan.Define(Guid.NewGuid(), "NoDb", 5_120, 5, 2, 3, 0, 5, 5, AccountFeature.Cron);
        var (planId, _) = await EndpointHarness.SeedAccountAsync(factory, "olive", plan);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var refused = await admin.PutAsJsonAsync($"/api/v1/plans/{planId}", Body("NoDb", ["telepathy"]));
        var unknownNumber = await admin.PutAsJsonAsync($"/api/v1/plans/{planId}", Body("NoDb", [63]));

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownNumber.StatusCode);
        var read = await admin.GetAsync($"/api/v1/plans/{planId}");
        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(
            ["cron"],
            body.RootElement.GetProperty("features").EnumerateArray().Select(feature =>
            {
                return feature.GetString();
            }));
    }

    /// <summary>The feature list is the administrator's to read and names every feature, localized.</summary>
    [Fact]
    public async Task The_feature_list_is_the_administrators_to_read_and_names_every_feature()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedCustomerAsync(factory, "olive", accountId);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        using var customer = await EndpointHarness.SignInAsync(factory, "olive");

        var listed = await admin.GetAsync("/api/v1/plans/features");
        var refused = await customer.GetAsync("/api/v1/plans/features");

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var body = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        var rows = body.RootElement.EnumerateArray().ToList();
        Assert.Equal(
            ["mail", "dns", "databases", "cron", "fileTransfer", "backups", "sites", "ssl", "tasks", "php"],
            rows.Select(row =>
            {
                return row.GetProperty("feature").GetString();
            }));
        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("featureDisplayName").GetString()));
            Assert.DoesNotContain("AccountFeature", row.GetProperty("featureDisplayName").GetString(), StringComparison.Ordinal);
        });
    }

    /// <summary>A plan body that leaves the features out is refused with 400 on create and on edit, and nothing changes.</summary>
    [Fact]
    public async Task A_plan_body_without_features_is_refused_on_create_and_on_edit()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var plan = Plan.Define(Guid.NewGuid(), "NoDb", 5_120, 5, 2, 3, 0, 5, 5, AccountFeature.Cron);
        var (planId, _) = await EndpointHarness.SeedAccountAsync(factory, "olive", plan);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var withoutFeatures = new
        {
            Name = "Fresh",
            DiskQuotaMb = 5_120,
            MaxSites = 5,
            MaxDatabases = 2,
            MaxSftpUsers = 3,
            MaxFtpUsers = 0,
            MaxCronEntries = 5,
            MaxPhpWorkersPerPool = 5,
            CpuQuotaPercent = new { Kind = "unlimited" },
            MemoryMaxMb = new { Kind = "unlimited" },
            MaxProcesses = new { Kind = "unlimited" },
        };

        var created = await admin.PostAsJsonAsync("/api/v1/plans", withoutFeatures);
        var edited = await admin.PutAsJsonAsync($"/api/v1/plans/{planId}", withoutFeatures);

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, edited.StatusCode);
        var read = await admin.GetAsync($"/api/v1/plans/{planId}");
        using var body = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(["cron"], body.RootElement.GetProperty("features").EnumerateArray().Select(feature =>
        {
            return feature.GetString();
        }));
    }

    /// <summary>
    /// Migrates the two modules whose list endpoints the plan-feature gate is probed through. Their
    /// lists count rows, so a customer a plan lets through reads a table that has to exist.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateListedModulesAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BackupsDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<DatabasesDbContext>().Database.MigrateAsync();
    }

    /// <summary>A plan body that changes nothing but the features it states.</summary>
    /// <param name="name">The plan's name.</param>
    /// <param name="features">The features to state, as JSON values.</param>
    private static object Body(string name, object[] features)
    {
        return new
        {
            Name = name,
            DiskQuotaMb = 5_120,
            MaxSites = 5,
            MaxDatabases = 2,
            MaxSftpUsers = 3,
            MaxFtpUsers = 0,
            MaxCronEntries = 5,
            MaxPhpWorkersPerPool = 5,
            CpuQuotaPercent = new { Kind = "unlimited" },
            MemoryMaxMb = new { Kind = "unlimited" },
            MaxProcesses = new { Kind = "unlimited" },
            Features = features,
        };
    }
}
