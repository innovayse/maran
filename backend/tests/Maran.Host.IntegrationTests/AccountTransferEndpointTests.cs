using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The real HTTP surface of handing an account to another owner (issue #369): who may call it, what is
/// stored, what each side then sees, what is journalled, and that the former reseller's view-as session
/// ends with the ownership while the customer's own session does not.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class AccountTransferEndpointTests : IAsyncLifetime
{
    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AccountTransferEndpointTests(PostgresFixture postgres)
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

    /// <summary>An administrator hands alpha's account to bravo: it is stored, journalled, and moves between the two lists.</summary>
    [Fact]
    public async Task An_administrator_hands_an_account_to_another_reseller()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");
        using var alpha = await world.SignInAsync("alpha");
        using var bravo = await world.SignInAsync("bravo");

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/accounts/{world.Alpha.AccountId}/owner",
            new { ResellerId = world.Bravo.ResellerId, PlanId = world.Bravo.PlanId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(world.Bravo.ResellerId, body.RootElement.GetProperty("resellerId").GetGuid());
        Assert.Equal("bravo", body.RootElement.GetProperty("resellerName").GetString());
        Assert.Equal(world.Bravo.PlanId, body.RootElement.GetProperty("planId").GetGuid());
        var stored = await world.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(account => account.Id == world.Alpha.AccountId);
        });
        Assert.Equal(world.Bravo.ResellerId, stored.ResellerId);
        Assert.Equal(world.Bravo.PlanId, stored.PlanId);

        // The former owner's answer for the account is now the one for an id that does not exist.
        var former = await alpha.GetAsync($"/api/v1/accounts/{world.Alpha.AccountId}");
        Assert.Equal(HttpStatusCode.NotFound, former.StatusCode);
        Assert.Equal("AccountNotFound", await ProblemDocument.CodeAsync(former));
        var current = await bravo.GetAsync($"/api/v1/accounts/{world.Alpha.AccountId}");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);

        var entry = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.AccountOwnerChanged));
        Assert.True(entry.Succeeded);
        Assert.Equal("admin", entry.ActorUsername);
        Assert.Equal("alphaone: alpha -> bravo (plan AlphaPlan -> BravoPlan)", entry.Subject);
    }

    /// <summary>An administrator hands a reseller's account to itself with its own plan.</summary>
    [Fact]
    public async Task An_administrator_takes_a_resellers_account_for_itself()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/accounts/{world.Alpha.AccountId}/owner",
            new { ResellerId = (Guid?)null, PlanId = world.HousePlanId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("resellerId").ValueKind);
        Assert.Equal(world.HousePlanId, body.RootElement.GetProperty("planId").GetGuid());
    }

    /// <summary>A plan of another owner than the new one is refused with 409 and nothing is stored.</summary>
    [Fact]
    public async Task A_plan_of_another_owner_is_refused_with_conflict_and_nothing_is_stored()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        var response = await admin.PutAsJsonAsync(
            $"/api/v1/accounts/{world.Alpha.AccountId}/owner",
            new { ResellerId = world.Bravo.ResellerId, PlanId = world.Alpha.PlanId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("PlanOwnerMismatch", await ProblemDocument.CodeAsync(response));
        var owner = await world.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.IgnoreQueryFilters().AsNoTracking()
                .Where(account => account.Id == world.Alpha.AccountId)
                .Select(account => account.ResellerId)
                .SingleAsync();
        });
        Assert.Equal(world.Alpha.ResellerId, owner);
        Assert.False(Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.AccountOwnerChanged)).Succeeded);
    }

    /// <summary>A reseller is refused the route with 403, for its own account as for another's, and nothing moves.</summary>
    [Fact]
    public async Task A_reseller_is_refused_the_route_for_its_own_account_and_for_another()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var own = await alpha.PutAsJsonAsync(
            $"/api/v1/accounts/{world.Alpha.AccountId}/owner",
            new { ResellerId = world.Bravo.ResellerId, PlanId = world.Bravo.PlanId });
        var foreign = await alpha.PutAsJsonAsync(
            $"/api/v1/accounts/{world.Bravo.AccountId}/owner",
            new { ResellerId = world.Alpha.ResellerId, PlanId = world.Alpha.PlanId });

        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Empty(await world.AuditEntriesAsync(AccountAuditActions.AccountOwnerChanged));
    }

    /// <summary>A customer and an anonymous caller are refused the route.</summary>
    [Fact]
    public async Task A_customer_and_an_anonymous_caller_are_refused_the_route()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        await EndpointHarness.SeedCustomerAsync(world.Factory, "olive", world.Alpha.AccountId);
        using var olive = await world.SignInAsync("olive");
        using var anonymous = world.Factory.CreateClient();
        var route = $"/api/v1/accounts/{world.Alpha.AccountId}/owner";
        var body = new { ResellerId = world.Bravo.ResellerId, PlanId = world.Bravo.PlanId };

        var asCustomer = await olive.PutAsJsonAsync(route, body);
        var asAnonymous = await anonymous.PutAsJsonAsync(route, body);

        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, asAnonymous.StatusCode);
    }

    /// <summary>The view-as session the former reseller holds on the account ends; the customer's own session does not.</summary>
    [Fact]
    public async Task The_former_resellers_view_as_session_ends_and_the_customers_own_session_survives()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        await EndpointHarness.SeedCustomerAsync(world.Factory, "olive", world.Alpha.AccountId);
        using var alpha = await world.SignInAsync("alpha");
        using var olive = await world.SignInAsync("olive");
        using var admin = await world.SignInAsync("admin");
        var started = await alpha.PostAsJsonAsync("/api/v1/view-as", new { AccountId = world.Alpha.AccountId });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);

        var moved = await admin.PutAsJsonAsync(
            $"/api/v1/accounts/{world.Alpha.AccountId}/owner",
            new { ResellerId = world.Bravo.ResellerId, PlanId = world.Bravo.PlanId });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        var revoked = await world.IdentityAsync(async identity =>
        {
            return await identity.Sessions.AsNoTracking()
                .Where(session => session.ViewAs != null
                    && session.ActiveAccountId == world.Alpha.AccountId
                    && session.Revocation != null)
                .Select(session => session.Revocation!.Reason)
                .ToListAsync();
        });
        Assert.Equal([SessionRevocationReason.AccountTransferred], revoked);

        // The customer's own token still reads its account: the move ended nothing of theirs.
        var own = await olive.GetAsync("/api/v1/accounts/me");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    /// <summary>
    /// A view-as session the former reseller opens while the move is half-done, after the agent was told
    /// and before the row is written, is refused on its first request once the move commits (issue #460).
    /// The planted break is the old order: it revoked before the push and never again, so this session
    /// survived and read the account as its owner's login.
    /// </summary>
    [Fact]
    public async Task A_view_as_session_started_during_the_move_is_refused_once_the_move_commits()
    {
        var agent = new ResellerAgentAccountsClient();
        await using var world = await new ResellerFixtureBuilder(_pg).WithAgent(agent).BuildAsync();
        await EndpointHarness.SeedCustomerAsync(world.Factory, "olive", world.Alpha.AccountId);
        await GrowPlanAsync(world, world.Bravo.PlanId);
        using var alpha = await world.SignInAsync("alpha");
        using var admin = await world.SignInAsync("admin");
        string? lateToken = null;
        agent.DuringQuotaPush = async () =>
        {
            var started = await alpha.PostAsJsonAsync("/api/v1/view-as", new { AccountId = world.Alpha.AccountId });
            Assert.Equal(HttpStatusCode.OK, started.StatusCode);
            using var body = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
            lateToken = body.RootElement.GetProperty("accessToken").GetString();
        };

        var moved = await admin.PutAsJsonAsync(
            $"/api/v1/accounts/{world.Alpha.AccountId}/owner",
            new { ResellerId = world.Bravo.ResellerId, PlanId = world.Bravo.PlanId });

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.NotEmpty(agent.Quotas);
        Assert.NotNull(lateToken);
        using var late = ClientWith(world, lateToken!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await late.GetAsync("/api/v1/accounts/me")).StatusCode);
    }

    /// <summary>
    /// Inverse control of the race above: the same session, started the same way, reads the account while
    /// the move has not happened, and the NEW owner's view-as session on the moved account reads it too, so
    /// the refusal above is the ownership change and not a check that refuses every view-as token.
    /// </summary>
    [Fact]
    public async Task A_view_as_session_of_the_owner_reads_the_account_and_the_new_owners_does_after_the_move()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        await EndpointHarness.SeedCustomerAsync(world.Factory, "olive", world.Alpha.AccountId);
        using var alpha = await world.SignInAsync("alpha");
        using var bravo = await world.SignInAsync("bravo");
        using var admin = await world.SignInAsync("admin");
        using var before = ClientWith(world, await StartViewAsTokenAsync(alpha, world.Alpha.AccountId));
        Assert.Equal(HttpStatusCode.OK, (await before.GetAsync("/api/v1/accounts/me")).StatusCode);

        var moved = await admin.PutAsJsonAsync(
            $"/api/v1/accounts/{world.Alpha.AccountId}/owner",
            new { ResellerId = world.Bravo.ResellerId, PlanId = world.Bravo.PlanId });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        using var after = ClientWith(world, await StartViewAsTokenAsync(bravo, world.Alpha.AccountId));
        Assert.Equal(HttpStatusCode.OK, (await after.GetAsync("/api/v1/accounts/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await before.GetAsync("/api/v1/accounts/me")).StatusCode);
    }

    /// <summary>Sells bravo's plan more disk than alpha's, so a move onto it pushes a quota to the agent.</summary>
    /// <param name="world">The seeded world.</param>
    /// <param name="planId">The plan to grow.</param>
    /// <returns>Resolves once the plan is stored.</returns>
    private static async Task GrowPlanAsync(ResellerWorld world, Guid planId)
    {
        await world.AccountsAsync(async accounts =>
        {
            var plan = await accounts.Plans.IgnoreQueryFilters().SingleAsync(p => p.Id == planId);
            plan.ChangeLimits(
                plan.DiskQuotaMb + 1_024,
                plan.MaxSites,
                plan.MaxDatabases,
                plan.MaxSftpUsers,
                plan.MaxFtpUsers,
                plan.MaxCronEntries,
                plan.MaxPhpWorkersPerPool);
            await accounts.SaveChangesAsync();
            return 0;
        });
    }

    /// <summary>Starts a view-as session on an account and returns its access token.</summary>
    /// <param name="operatorClient">The signed-in operator.</param>
    /// <param name="accountId">The account to view as.</param>
    /// <returns>The view-as access token.</returns>
    private static async Task<string> StartViewAsTokenAsync(HttpClient operatorClient, Guid accountId)
    {
        var started = await operatorClient.PostAsJsonAsync("/api/v1/view-as", new { AccountId = accountId });
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var body = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("accessToken").GetString()!;
    }

    /// <summary>A client carrying a bearer token.</summary>
    /// <param name="world">The seeded world.</param>
    /// <param name="accessToken">The token.</param>
    /// <returns>A client the caller disposes.</returns>
    private static HttpClient ClientWith(ResellerWorld world, string accessToken)
    {
        var client = world.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
