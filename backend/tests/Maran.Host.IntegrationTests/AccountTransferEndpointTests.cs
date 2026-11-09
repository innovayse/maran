using System.Net;
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
}
