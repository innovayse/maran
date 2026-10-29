using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Plan management over real HTTP against a real PostgreSQL: who may reach it, what a create, edit and
/// delete do to the stored row and to the journal, and — the part the in-memory unit tests cannot
/// prove — that the database itself holds the line when a handler's check is bypassed: the
/// <c>ON DELETE RESTRICT</c> foreign key, the unique name index and the name-mirrors-key constraint.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class PlansEndpointTests : IAsyncLifetime
{
    /// <summary>One mebibyte in bytes, the unit a quota is pushed to the agent in.</summary>
    private const ulong Mebibyte = 1024UL * 1024UL;

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PlansEndpointTests(PostgresFixture postgres)
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

    /// <summary>A request body for a plan, with every limit different from every other.</summary>
    /// <param name="name">The plan's name.</param>
    /// <param name="diskQuotaMb">The disk quota to ask for.</param>
    private static object Body(string name, int diskQuotaMb = 10_240)
    {
        return new
        {
            Name = name,
            DiskQuotaMb = diskQuotaMb,
            MaxSites = 10,
            MaxDatabases = 5,
            MaxSftpUsers = 4,
            MaxFtpUsers = 2,
            MaxCronEntries = 3,
            MaxPhpWorkersPerPool = 8,
        };
    }

    /// <summary>Every plan route refuses a customer with 403 and an anonymous caller with 401.</summary>
    [Theory]
    [InlineData("GET", false)]
    [InlineData("POST", false)]
    [InlineData("PUT", false)]
    [InlineData("DELETE", false)]
    [InlineData("GET", true)]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("DELETE", true)]
    public async Task Every_plan_route_refuses_a_customer_with_forbidden_and_an_anonymous_caller_with_unauthorized(
        string verb,
        bool anonymous)
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (planId, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedCustomerAsync(factory, "olive", accountId);
        using var client = anonymous ? factory.CreateClient() : await EndpointHarness.SignInAsync(factory, "olive");
        var url = verb is "PUT" or "DELETE" ? $"/api/v1/plans/{planId}" : "/api/v1/plans";

        using var request = new HttpRequestMessage(new HttpMethod(verb), url);
        if (verb is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(Body("Intruder"));
        }

        var response = await client.SendAsync(request);

        Assert.Equal(anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        Assert.Equal(1, await accounts.Plans.CountAsync());
    }

    /// <summary>An administrator creates a plan, lists it, edits it and deletes it, each journalled.</summary>
    [Fact]
    public async Task An_administrator_creates_a_plan_lists_it_edits_it_and_deletes_it_each_journalled()
    {
        // The inverse control for the refusals above: the same routes answer the administrator.
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var created = await admin.PostAsJsonAsync("/api/v1/plans", Body("Agency"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdBody.RootElement.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/plans/{id}", created.Headers.Location?.ToString());
        Assert.Equal("Agency", createdBody.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(0, createdBody.RootElement.GetProperty("accountCount").GetInt32());

        var listed = await ListAsync(admin);
        var listedPlan = Assert.Single(listed);
        Assert.Equal(id, listedPlan.GetProperty("id").GetGuid());
        Assert.Equal(2, listedPlan.GetProperty("maxFtpUsers").GetInt32());
        Assert.Equal(3, listedPlan.GetProperty("maxCronEntries").GetInt32());
        Assert.Equal(8, listedPlan.GetProperty("maxPhpWorkersPerPool").GetInt32());

        var edited = await admin.PutAsJsonAsync($"/api/v1/plans/{id}", Body("Studio", diskQuotaMb: 20_480));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using var editedBody = JsonDocument.Parse(await edited.Content.ReadAsStringAsync());
        Assert.Equal("Studio", editedBody.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(20_480, editedBody.RootElement.GetProperty("diskQuotaMb").GetInt32());

        var deleted = await admin.DeleteAsync($"/api/v1/plans/{id}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Empty(await ListAsync(admin));

        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var entries = await identity.AuditEvents.AsNoTracking()
            .Where(e => e.Action == AuditActions.PlanCreated || e.Action == AuditActions.PlanUpdated || e.Action == AuditActions.PlanDeleted)
            .OrderBy(e => e.OccurredAt)
            .ToListAsync();
        Assert.Equal(
            [
                (AuditActions.PlanCreated, "Agency", true),
                (AuditActions.PlanUpdated, "Agency -> Studio", true),
                (AuditActions.PlanDeleted, "Studio", true),
            ],
            entries.Select(e =>
            {
                return (e.Action, e.Subject, e.Succeeded);
            }));
        Assert.All(entries, entry =>
        {
            Assert.Equal("admin", entry.ActorUsername);
        });
    }

    /// <summary>An invalid plan is refused with 400 and nothing is stored.</summary>
    [Fact]
    public async Task An_invalid_plan_is_refused_with_bad_request_and_nothing_is_stored()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.PostAsJsonAsync("/api/v1/plans", Body("Agency", diskQuotaMb: 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ListAsync(admin));
    }

    /// <summary>A name already taken is refused with 409 whatever its case.</summary>
    [Fact]
    public async Task A_name_already_taken_is_refused_with_conflict_whatever_its_case()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        await admin.PostAsJsonAsync("/api/v1/plans", Body("Agency"));

        var response = await admin.PostAsJsonAsync("/api/v1/plans", Body("AGENCY"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("PlanNameTaken", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single(await ListAsync(admin));
    }

    /// <summary>Editing or deleting a plan that does not exist answers 404.</summary>
    [Fact]
    public async Task Editing_or_deleting_a_plan_that_does_not_exist_answers_not_found()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");
        var missing = Guid.NewGuid();

        var edit = await admin.PutAsJsonAsync($"/api/v1/plans/{missing}", Body("Studio"));
        var delete = await admin.DeleteAsync($"/api/v1/plans/{missing}");

        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    /// <summary>A plan an account is on is refused deletion with 409 and survives.</summary>
    [Fact]
    public async Task A_plan_an_account_is_on_is_refused_deletion_with_conflict_and_survives()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (planId, _) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.DeleteAsync($"/api/v1/plans/{planId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("PlanInUse", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var plan = Assert.Single(await ListAsync(admin));
        Assert.Equal(1, plan.GetProperty("accountCount").GetInt32());
    }

    /// <summary>The database refuses to delete a plan an account is on even when no handler checked first.</summary>
    [Fact]
    public async Task The_database_refuses_to_delete_a_plan_an_account_is_on_even_when_no_handler_checked_first()
    {
        // The backstop behind the handler's check, and what holds against a race with an account
        // created between that check and the delete: ON DELETE RESTRICT on FK_Accounts_Plans_PlanId.
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        var (planId, _) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();
        accounts.Plans.Remove(await accounts.Plans.SingleAsync(plan => plan.Id == planId));

        var failure = await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await accounts.SaveChangesAsync();
        });

        var postgres = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgres.SqlState);
        Assert.Equal("FK_Accounts_Plans_PlanId", postgres.ConstraintName);
    }

    /// <summary>The database holds a plan's name and key equal and a name unique, whatever wrote the row.</summary>
    [Fact]
    public async Task The_database_holds_a_plans_name_and_key_equal_and_a_name_unique_whatever_wrote_the_row()
    {
        await using var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountsDbContext>();

        var mismatched = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            // raw-sql: a row no entity can produce — the name disagrees with the key — to prove the
            // check constraint refuses it; parameters carry the values.
            await accounts.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into accounts."Plans" ("Id","DisplayNameKey","Name","DiskQuotaMb","MaxSites","MaxDatabases","MaxSftpUsers","MaxFtpUsers","MaxCronEntries","MaxPhpWorkersPerPool")
                values ({Guid.NewGuid()}, 'Key', 'Other', 1, 1, 1, 1, 0, 0, 1)
                """);
        });
        Assert.Equal(PostgresErrorCodes.CheckViolation, mismatched.SqlState);
        Assert.Equal("CK_Plans_NameMirroredInKey", mismatched.ConstraintName);

        accounts.Plans.Add(Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8));
        await accounts.SaveChangesAsync();
        accounts.Plans.Add(Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await accounts.SaveChangesAsync();
        });
        Assert.Equal("IX_Plans_DisplayNameKey", Assert.IsType<PostgresException>(duplicate.InnerException).ConstraintName);
    }

    /// <summary>Lowering a quota below an account's usage is refused with 409 and nothing is changed.</summary>
    [Fact]
    public async Task Lowering_a_quota_below_an_accounts_usage_is_refused_with_conflict_and_nothing_is_changed()
    {
        var agent = new FixedUsageAgentAccountsClient(usedBytes: 6_144 * Mebibyte);
        await using var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.RemoveAll<IAgentAccountsClient>();
            services.AddSingleton<IAgentAccountsClient>(agent);
        });
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);
        await EndpointHarness.SeedAccountAsync(factory, "olive", plan);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.PutAsJsonAsync($"/api/v1/plans/{plan.Id}", Body("Agency", diskQuotaMb: 5_120));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("PlanQuotaBelowUsage", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(agent.QuotasSet);
        Assert.Equal(10_240, Assert.Single(await ListAsync(admin)).GetProperty("diskQuotaMb").GetInt32());
    }

    /// <summary>Raising a quota is pushed to the account on the host and stored.</summary>
    [Fact]
    public async Task Raising_a_quota_is_pushed_to_the_account_on_the_host_and_stored()
    {
        var agent = new FixedUsageAgentAccountsClient(usedBytes: 6_144 * Mebibyte);
        await using var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.RemoveAll<IAgentAccountsClient>();
            services.AddSingleton<IAgentAccountsClient>(agent);
        });
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var plan = Plan.Define(Guid.NewGuid(), "Agency", 10_240, 10, 5, 4, 2, 3, 8);
        await EndpointHarness.SeedAccountAsync(factory, "olive", plan);
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.PutAsJsonAsync($"/api/v1/plans/{plan.Id}", Body("Agency", diskQuotaMb: 20_480));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([$"olive:{20_480 * Mebibyte}"], agent.QuotasSet);
        Assert.Equal(20_480, Assert.Single(await ListAsync(admin)).GetProperty("diskQuotaMb").GetInt32());
    }

    /// <summary>Reads the plan list as an administrator.</summary>
    /// <param name="admin">A client signed in as the administrator.</param>
    private static async Task<List<JsonElement>> ListAsync(HttpClient admin)
    {
        var response = await admin.GetAsync("/api/v1/plans");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray().Select(element =>
        {
            return element.Clone();
        }).ToList();
    }
}
