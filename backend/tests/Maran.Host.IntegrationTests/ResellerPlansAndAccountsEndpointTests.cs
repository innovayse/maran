using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// What a reseller does with its own plans and accounts, and what an administrator does for it, over
/// real HTTP against a real PostgreSQL (issue #179): the operations a reseller may run inside its
/// envelope, who owns what is created, which plan an account may be on, and which features a plan may
/// offer — each action journalled with the right actor.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class ResellerPlansAndAccountsEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerPlansAndAccountsEndpointTests(PostgresFixture postgres)
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

    /// <summary>
    /// A reseller creates a plan, renames it and deletes it; each answer is the right one, the plan is
    /// owned by the reseller throughout, and each of the three actions is journalled under the
    /// reseller's name.
    /// </summary>
    [Fact]
    public async Task A_reseller_creates_renames_and_deletes_its_own_plan_each_journalled_under_its_name()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var created = await alpha.PostAsJsonAsync("/api/v1/plans", ResellerRequestBodies.Plan("Gamma"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdBody.RootElement.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/plans/{id}", created.Headers.Location?.ToString());
        Assert.Equal(world.Alpha.ResellerId, createdBody.RootElement.GetProperty("resellerId").GetGuid());

        var renamed = await alpha.PutAsJsonAsync($"/api/v1/plans/{id}", ResellerRequestBodies.Plan("Delta"));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        using var renamedBody = JsonDocument.Parse(await renamed.Content.ReadAsStringAsync());
        Assert.Equal("Delta", renamedBody.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("alpha", renamedBody.RootElement.GetProperty("resellerName").GetString());

        var deleted = await alpha.DeleteAsync($"/api/v1/plans/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var entries = await world.AuditEntriesAsync(
            AccountAuditActions.PlanCreated, AccountAuditActions.PlanUpdated, AccountAuditActions.PlanDeleted);
        Assert.Equal(
            [
                (AccountAuditActions.PlanCreated, "Gamma", true),
                (AccountAuditActions.PlanUpdated, "Gamma -> Delta", true),
                (AccountAuditActions.PlanDeleted, "Delta", true),
            ],
            entries.Select(entry => { return (entry.Action, entry.Subject, entry.Succeeded); }));
        Assert.All(entries, entry => { Assert.Equal("alpha", entry.ActorUsername); });
    }

    /// <summary>
    /// A reseller refused deletion of a plan one of its accounts is on gets 409 <c>PlanInUse</c>, and the
    /// plan survives; the refusal is journalled.
    /// </summary>
    [Fact]
    public async Task A_reseller_cannot_delete_a_plan_one_of_its_accounts_is_on()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var response = await alpha.DeleteAsync($"/api/v1/plans/{world.Alpha.PlanId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("PlanInUse", await ProblemDocument.CodeAsync(response));
        Assert.Equal(1, await world.AccountsAsync(async accounts =>
        {
            return await accounts.Plans.IgnoreQueryFilters().CountAsync(plan => plan.Id == world.Alpha.PlanId);
        }));
        var refused = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.PlanDeleted));
        Assert.Equal(("alpha", false), (refused.ActorUsername, refused.Succeeded));
    }

    /// <summary>
    /// A reseller creates an account on its own plan: the account is the reseller's, the host is asked
    /// once, the owner is invited by mail; the reseller then changes the owner's address. Both are
    /// journalled with the reseller as actor.
    /// </summary>
    [Fact]
    public async Task A_reseller_creates_an_account_on_its_own_plan_and_changes_the_owner_address()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var created = await alpha.PostAsJsonAsync(
            "/api/v1/accounts", ResellerRequestBodies.Account("alphatwo", world.Alpha.PlanId));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdBody.RootElement.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/accounts/{id}", created.Headers.Location?.ToString());
        Assert.Equal(world.Alpha.ResellerId, createdBody.RootElement.GetProperty("resellerId").GetGuid());
        Assert.Equal("alpha", createdBody.RootElement.GetProperty("resellerName").GetString());
        Assert.Equal(["alphatwo"], world.Agent.Created);
        await SubUserTeamWorld.MailToAsync(world.Mailer, "alphatwo@example.com");

        var changed = await alpha.PutAsJsonAsync(
            $"/api/v1/accounts/{id}/owner-email", new { OwnerEmail = "alphatwo-new@example.com" });

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var changedBody = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
        Assert.Equal("alphatwo-new@example.com", changedBody.RootElement.GetProperty("ownerEmail").GetString());
        var entries = await world.AuditEntriesAsync(
            AccountAuditActions.AccountCreated, AccountAuditActions.AccountOwnerEmailChanged);
        Assert.Equal(
            [
                (AccountAuditActions.AccountCreated, "alphatwo", true),
                (AccountAuditActions.AccountOwnerEmailChanged, "alphatwo", true),
            ],
            entries.Select(entry => { return (entry.Action, entry.Subject, entry.Succeeded); }));
        Assert.All(entries, entry => { Assert.Equal("alpha", entry.ActorUsername); });
    }

    /// <summary>
    /// A reseller that names another reseller as the owner of a new account is told 404
    /// <c>ResellerNotFound</c> and nothing is created; naming itself, or nobody, makes the account its
    /// own. A reseller can therefore never create an account for someone else.
    /// </summary>
    [Fact]
    public async Task A_reseller_cannot_create_an_account_for_another_reseller_and_stamps_itself_otherwise()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var foreign = await alpha.PostAsJsonAsync(
            "/api/v1/accounts", ResellerRequestBodies.Account("alphatwo", world.Alpha.PlanId, world.Bravo.ResellerId));
        var named = await alpha.PostAsJsonAsync(
            "/api/v1/accounts", ResellerRequestBodies.Account("alphatwo", world.Alpha.PlanId, world.Alpha.ResellerId));
        var unnamed = await alpha.PostAsJsonAsync(
            "/api/v1/accounts", ResellerRequestBodies.Account("alphathree", world.Alpha.PlanId));

        Assert.Equal((HttpStatusCode.NotFound, "ResellerNotFound"), await ProblemDocument.SignatureAsync(foreign));
        Assert.Equal(HttpStatusCode.Created, named.StatusCode);
        Assert.Equal(HttpStatusCode.Created, unnamed.StatusCode);
        var owners = await world.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.IgnoreQueryFilters().AsNoTracking()
                .Where(account => account.Name == "alphatwo" || account.Name == "alphathree")
                .Select(account => account.ResellerId)
                .ToListAsync();
        });
        Assert.Equal([world.Alpha.ResellerId, world.Alpha.ResellerId], owners);
        Assert.Equal(["alphatwo", "alphathree"], world.Agent.Created);
    }

    /// <summary>
    /// An account name is one namespace across every tenant: a reseller creating an account named like
    /// another reseller is told the name is taken. This pins the accepted residual — a conflict is the
    /// only honest answer to a name every tenant shares — so it cannot change unnoticed.
    /// </summary>
    [Fact]
    public async Task An_account_name_that_another_reseller_holds_is_taken_for_every_tenant()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var response = await alpha.PostAsJsonAsync(
            "/api/v1/accounts", ResellerRequestBodies.Account("bravo", world.Alpha.PlanId));

        Assert.Equal((HttpStatusCode.Conflict, "AccountNameTaken"), await ProblemDocument.SignatureAsync(response));
        Assert.Empty(world.Agent.Created);
    }

    /// <summary>
    /// An administrator creates accounts for a reseller and for itself, and an account is always on a plan
    /// its owner owns: the reseller's account on the reseller's plan and the administrator's on the
    /// administrator's are created, and every other pairing — a reseller's account on the administrator's
    /// plan, the administrator's on a reseller's, an unknown reseller — is refused before the host is
    /// asked, each with its own code, and journalled.
    /// </summary>
    [Fact]
    public async Task An_account_is_only_ever_on_a_plan_its_owner_owns()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        var forReseller = await admin.PostAsJsonAsync(
            "/api/v1/accounts", ResellerRequestBodies.Account("alphatwo", world.Alpha.PlanId, world.Alpha.ResellerId));
        var forAdministrator = await admin.PostAsJsonAsync(
            "/api/v1/accounts", ResellerRequestBodies.Account("housetwo", world.HousePlanId));
        var resellerOnHouse = await admin.PostAsJsonAsync(
            "/api/v1/accounts", ResellerRequestBodies.Account("mismatchone", world.HousePlanId, world.Alpha.ResellerId));
        var houseOnReseller = await admin.PostAsJsonAsync(
            "/api/v1/accounts", ResellerRequestBodies.Account("mismatchtwo", world.Alpha.PlanId));
        var unknownReseller = await admin.PostAsJsonAsync(
            "/api/v1/accounts", ResellerRequestBodies.Account("ownerless", world.Alpha.PlanId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Created, forReseller.StatusCode);
        using var forResellerBody = JsonDocument.Parse(await forReseller.Content.ReadAsStringAsync());
        Assert.Equal(world.Alpha.ResellerId, forResellerBody.RootElement.GetProperty("resellerId").GetGuid());
        Assert.Equal(HttpStatusCode.Created, forAdministrator.StatusCode);
        using var forAdministratorBody = JsonDocument.Parse(await forAdministrator.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, forAdministratorBody.RootElement.GetProperty("resellerId").ValueKind);
        Assert.Equal((HttpStatusCode.Conflict, "PlanOwnerMismatch"), await ProblemDocument.SignatureAsync(resellerOnHouse));
        Assert.Equal((HttpStatusCode.Conflict, "PlanOwnerMismatch"), await ProblemDocument.SignatureAsync(houseOnReseller));
        Assert.Equal((HttpStatusCode.NotFound, "ResellerNotFound"), await ProblemDocument.SignatureAsync(unknownReseller));
        Assert.Equal(["alphatwo", "housetwo"], world.Agent.Created);
        var entries = await world.AuditEntriesAsync(AccountAuditActions.AccountCreated);
        Assert.Equal(
            [
                ("alphatwo", true),
                ("housetwo", true),
                ("mismatchone", false),
                ("mismatchtwo", false),
                ("ownerless", false),
            ],
            entries.Select(entry => { return (entry.Subject, entry.Succeeded); }));
        Assert.All(entries, entry => { Assert.Equal("admin", entry.ActorUsername); });
    }

    /// <summary>
    /// The feature list a plan form offers is the whole catalogue to an administrator and only what the
    /// envelope sold to a reseller, so a reseller is never offered a feature it would be refused.
    /// </summary>
    [Fact]
    public async Task The_feature_list_is_the_whole_catalogue_to_an_administrator_and_the_envelope_to_a_reseller()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");
        using var alpha = await world.SignInAsync("alpha");

        var offeredToReseller = await FeaturesAsync(alpha);
        var offeredToAdministrator = await FeaturesAsync(admin);

        Assert.Equal(ResellerEnvelopeSpec.SoldFeatureNames.Order(), offeredToReseller.Order());
        Assert.Equal(11, offeredToAdministrator.Count);
        Assert.Subset(offeredToAdministrator.ToHashSet(), offeredToReseller.ToHashSet());
        Assert.Contains("sites", offeredToAdministrator);
        Assert.DoesNotContain("sites", offeredToReseller);
    }

    /// <summary>Reads the wire names of the features a plan form is offered.</summary>
    /// <param name="client">The caller's client.</param>
    /// <returns>The feature names.</returns>
    private static async Task<IReadOnlyList<string>> FeaturesAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/plans/features"));
        return body.RootElement.EnumerateArray()
            .Select(row =>
            {
                return row.GetProperty("feature").GetString()!;
            })
            .ToList();
    }
}
