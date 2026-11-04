using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// A reseller's life over real HTTP against a real PostgreSQL (issue #179): suspension refuses its login
/// and its writes and leaves its customers' accounts running (decision D2), reactivation undoes it,
/// deletion is refused while the reseller owns anything, and every one of them is journalled.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class ResellerLifecycleEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerLifecycleEndpointTests(PostgresFixture postgres)
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
    /// Suspending a reseller suspends the reseller and its login and nothing else: its customer's
    /// account stays active and its customer can still sign in, while the reseller cannot.
    /// </summary>
    [Fact]
    public async Task Suspending_a_reseller_refuses_its_login_and_leaves_its_customers_accounts_active()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        await EndpointHarness.SeedCustomerAsync(world.Factory, "olive", world.Alpha.AccountId);
        using var admin = await world.SignInAsync("admin");

        var response = await admin.PostAsync($"/api/v1/resellers/{world.Alpha.ResellerId}/suspend", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("suspended", body.RootElement.GetProperty("status").GetString());
        var reseller = await world.AccountsAsync(async accounts =>
        {
            return await accounts.Resellers.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.Id == world.Alpha.ResellerId);
        });
        Assert.Equal(ResellerStatus.Suspended, reseller.Status);
        var login = await world.IdentityAsync(async identity =>
        {
            return await identity.Users.AsNoTracking().SingleAsync(user => user.ResellerId == world.Alpha.ResellerId);
        });
        Assert.True(login.IsSuspended);
        Assert.False(login.MayAuthenticate);

        var account = await world.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == world.Alpha.AccountId);
        });
        Assert.Equal(AccountStatus.Active, account.Status);
        using var anonymous = world.Factory.CreateClient();
        var resellerSignIn = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "alpha", Password = EndpointHarness.Password });
        var customerSignIn = await anonymous.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "olive", Password = EndpointHarness.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, resellerSignIn.StatusCode);
        Assert.Equal(HttpStatusCode.OK, customerSignIn.StatusCode);

        var entry = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.ResellerSuspended));
        Assert.Equal(("admin", "alpha", true), (entry.ActorUsername, entry.Subject, entry.Succeeded));
    }

    /// <summary>
    /// Reactivating a suspended reseller lets its login sign in again and is journalled; the other
    /// reseller is untouched throughout.
    /// </summary>
    [Fact]
    public async Task Reactivating_a_reseller_lets_its_login_sign_in_again()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");
        await admin.PostAsync($"/api/v1/resellers/{world.Alpha.ResellerId}/suspend", null);

        var response = await admin.PostAsync($"/api/v1/resellers/{world.Alpha.ResellerId}/reactivate", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("active", body.RootElement.GetProperty("status").GetString());
        using var alpha = await world.SignInAsync("alpha");
        Assert.Equal(HttpStatusCode.OK, (await alpha.GetAsync("/api/v1/reseller/me")).StatusCode);
        using var bravo = await world.SignInAsync("bravo");
        Assert.Equal(HttpStatusCode.OK, (await bravo.GetAsync("/api/v1/reseller/me")).StatusCode);
        var entry = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.ResellerReactivated));
        Assert.Equal(("admin", "alpha", true), (entry.ActorUsername, entry.Subject, entry.Succeeded));
    }

    /// <summary>
    /// A reseller whose access token is still live when it is suspended can read and cannot write: every
    /// mutation answers 409 <c>ResellerSuspended</c>, nothing it owns changes, and each refusal is
    /// journalled with the reseller as actor. The token outlives the suspension by up to its lifetime;
    /// this is what stops it being a way to keep working.
    /// </summary>
    [Fact]
    public async Task A_suspended_resellers_live_token_reads_and_cannot_write()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var admin = await world.SignInAsync("admin");
        await admin.PostAsync($"/api/v1/resellers/{world.Alpha.ResellerId}/suspend", null);
        var plan = world.Alpha.PlanId;
        var account = world.Alpha.AccountId;

        Assert.Equal(HttpStatusCode.OK, (await alpha.GetAsync("/api/v1/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alpha.GetAsync("/api/v1/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alpha.GetAsync($"/api/v1/accounts/{account}")).StatusCode);

        var writes = new List<(string Name, HttpResponseMessage Response)>
        {
            ("create plan", await alpha.PostAsJsonAsync("/api/v1/plans", ResellerRequestBodies.Plan("Gamma"))),
            ("update plan", await alpha.PutAsJsonAsync($"/api/v1/plans/{plan}", ResellerRequestBodies.Plan("AlphaPlan"))),
            ("delete plan", await alpha.DeleteAsync($"/api/v1/plans/{plan}")),
            ("create account", await alpha.PostAsJsonAsync("/api/v1/accounts", ResellerRequestBodies.Account("alphatwo", plan))),
            ("change plan", await alpha.PutAsJsonAsync($"/api/v1/accounts/{account}/plan", new { PlanId = plan })),
            ("change owner", await alpha.PutAsJsonAsync($"/api/v1/accounts/{account}/owner-email", new { OwnerEmail = "x@example.com" })),
            ("suspend account", await alpha.PostAsync($"/api/v1/accounts/{account}/suspend", null)),
            ("reactivate account", await alpha.PostAsync($"/api/v1/accounts/{account}/reactivate", null)),
            ("delete account", await alpha.DeleteAsync($"/api/v1/accounts/{account}")),
        };
        foreach (var (name, response) in writes)
        {
            Assert.True(
                response.StatusCode == HttpStatusCode.Conflict
                    && await ProblemDocument.CodeAsync(response) == "ResellerSuspended",
                $"{name} was answered {(int)response.StatusCode}, not 409 ResellerSuspended.");
        }

        Assert.Equal(1, await world.AccountsAsync(async accounts =>
        {
            return await accounts.Plans.IgnoreQueryFilters().CountAsync(p => p.ResellerId == world.Alpha.ResellerId);
        }));
        Assert.Equal(1, await world.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.IgnoreQueryFilters().CountAsync(a => a.ResellerId == world.Alpha.ResellerId);
        }));
        Assert.Empty(world.Agent.Created);
        var refusals = await world.AuditEntriesAsync(
            AccountAuditActions.PlanCreated,
            AccountAuditActions.PlanUpdated,
            AccountAuditActions.PlanDeleted,
            AccountAuditActions.AccountCreated,
            AccountAuditActions.AccountPlanChanged,
            AccountAuditActions.AccountOwnerEmailChanged,
            AccountAuditActions.AccountSuspended,
            AccountAuditActions.AccountReactivated,
            AccountAuditActions.AccountDeleted);
        Assert.Equal(writes.Count, refusals.Count);
        Assert.All(refusals, entry =>
        {
            Assert.Equal("alpha", entry.ActorUsername);
            Assert.False(entry.Succeeded);
        });
    }

    /// <summary>
    /// Suspending, reactivating or deleting a reseller that does not exist answers 404
    /// <c>ResellerNotFound</c> and the refusal is journalled under the action that was attempted.
    /// </summary>
    /// <param name="verb">The HTTP verb.</param>
    /// <param name="suffix">The route after the id.</param>
    /// <param name="action">The audit action the refusal is journalled under.</param>
    [Theory]
    [InlineData("POST", "/suspend", AccountAuditActions.ResellerSuspended)]
    [InlineData("POST", "/reactivate", AccountAuditActions.ResellerReactivated)]
    [InlineData("DELETE", "", AccountAuditActions.ResellerDeleted)]
    public async Task An_unknown_reseller_is_not_found_and_the_refusal_is_journalled(
        string verb,
        string suffix,
        string action)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        using var request = new HttpRequestMessage(new HttpMethod(verb), $"/api/v1/resellers/{Guid.NewGuid()}{suffix}");
        var response = await admin.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ResellerNotFound", await ProblemDocument.CodeAsync(response));
        var refused = Assert.Single(await world.AuditEntriesAsync(action));
        Assert.Equal(("admin", false), (refused.ActorUsername, refused.Succeeded));
    }

    /// <summary>
    /// A reseller that still owns an account is refused deletion with 409 <c>ResellerHasAccounts</c>;
    /// so is one that owns only a plan. The reseller, its login and its rows survive, and the refusal is
    /// journalled under the reseller's name.
    /// </summary>
    /// <param name="ownsAccount">Whether the reseller owns an account (<c>alpha</c>) or only a plan.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_reseller_that_still_owns_an_account_or_a_plan_is_refused_deletion(bool ownsAccount)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");
        var name = "alpha";
        var id = world.Alpha.ResellerId;
        if (!ownsAccount)
        {
            // A reseller with a plan and no account: created through the endpoint, then given a plan.
            var created = await admin.PostAsJsonAsync("/api/v1/resellers", ResellerRequestBodies.Reseller("charlie"));
            using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            id = createdBody.RootElement.GetProperty("id").GetGuid();
            name = "charlie";
            await world.AccountsAsync(async accounts =>
            {
                accounts.Plans.Add(Plan.Define(
                    Guid.NewGuid(), "CharliePlan", 5_120, 5, 2, 3, 1, 2, 4, resellerId: id));
                return await accounts.SaveChangesAsync();
            });
        }

        var response = await admin.DeleteAsync($"/api/v1/resellers/{id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ResellerHasAccounts", await ProblemDocument.CodeAsync(response));
        Assert.Equal(1, await world.AccountsAsync(async accounts =>
        {
            return await accounts.Resellers.IgnoreQueryFilters().CountAsync(reseller => reseller.Id == id);
        }));
        Assert.Equal(1, await world.IdentityAsync(async identity =>
        {
            return await identity.Users.CountAsync(user => user.ResellerId == id);
        }));
        var refused = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.ResellerDeleted));
        Assert.Equal(("admin", name, false), (refused.ActorUsername, refused.Subject, refused.Succeeded));
    }

    /// <summary>
    /// A reseller that owns nothing is deleted with 204 together with its login, the reseller is then
    /// not found, and the deletion is journalled.
    /// </summary>
    [Fact]
    public async Task A_reseller_that_owns_nothing_is_deleted_with_its_login()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");
        var created = await admin.PostAsJsonAsync("/api/v1/resellers", ResellerRequestBodies.Reseller("charlie"));
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdBody.RootElement.GetProperty("id").GetGuid();

        var response = await admin.DeleteAsync($"/api/v1/resellers/{id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/resellers/{id}")).StatusCode);
        Assert.Equal(0, await world.IdentityAsync(async identity =>
        {
            return await identity.Users.CountAsync(user => user.ResellerId == id);
        }));
        var entry = Assert.Single(await world.AuditEntriesAsync(AccountAuditActions.ResellerDeleted));
        Assert.Equal(("admin", "charlie", true), (entry.ActorUsername, entry.Subject, entry.Succeeded));
    }
}
