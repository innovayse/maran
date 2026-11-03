using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The closing criterion of the reseller role (issue #179): reseller A cannot see, read, change,
/// enumerate or delete anything of reseller B, or of the administrator, and is told exactly what it is
/// told about an identifier that does not exist — 404, never 403 — over real HTTP against a real
/// PostgreSQL, in both directions between the two seeded resellers.
/// </summary>
/// <remarks>
/// <para>
/// <b>How "identical to a random identifier" is made able to fail.</b> Every probe is sent twice, once
/// at the foreign row and once at an identifier that exists nowhere, and the two answers are compared.
/// A 404 alone would pass over an endpoint that answered 404 for a foreign row and 404 with a different
/// code for a missing one, which is the difference an enumeration attack reads. The positive controls —
/// the reseller reading its OWN account, plan and lists — are what stop a route that refuses everything
/// from passing.
/// </para>
/// <para>
/// UNOBSERVED HERE: view-as into a foreign account, which has its own tests in the view-as area, and the
/// reseller's reach into tenant modules, which is "no rows" by the callers' own filters.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ResellerIsolationTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerIsolationTests(PostgresFixture postgres)
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

    /// <summary>Each seeded reseller in turn as the one that probes; the other is then the stranger.</summary>
    /// <returns>The probing reseller's name.</returns>
    public static TheoryData<string> ActingResellers()
    {
        return new TheoryData<string> { "alpha", "bravo" };
    }

    /// <summary>
    /// Every account endpoint answers a reseller that names another reseller's account, or the
    /// administrator's, exactly as it answers a random identifier — 404 <c>AccountNotFound</c> — and
    /// changes nothing; every refused mutation is journalled with the reseller as actor.
    /// </summary>
    /// <param name="actor">The reseller that probes.</param>
    [Theory]
    [MemberData(nameof(ActingResellers))]
    public async Task A_reseller_probing_an_account_it_does_not_own_is_answered_as_a_random_identifier_is(string actor)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        var self = world.Reseller(actor);
        var stranger = world.Reseller(actor == "alpha" ? "bravo" : "alpha");
        using var client = await world.SignInAsync(actor);
        var probes = new[]
        {
            new AccountProbe(HttpMethod.Get, string.Empty, null),
            new AccountProbe(HttpMethod.Get, "/resource-summary", null),
            new AccountProbe(HttpMethod.Post, "/suspend", null),
            new AccountProbe(HttpMethod.Post, "/reactivate", null),
            new AccountProbe(HttpMethod.Put, "/plan", new { self.PlanId }),
            new AccountProbe(HttpMethod.Put, "/owner-email", new { OwnerEmail = "probe@example.com" }),
            new AccountProbe(HttpMethod.Delete, string.Empty, null),
        };

        // Positive controls: the same two reads on the reseller's OWN account succeed.
        using var own = await client.GetAsync($"/api/v1/accounts/{self.AccountId}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/accounts/{self.AccountId}/resource-summary")).StatusCode);

        foreach (var target in new[] { stranger.AccountId, world.HouseAccountId })
        {
            foreach (var probe in probes)
            {
                var foreign = await ProblemDocument.SignatureAsync(await SendAsync(client, probe, target));
                var random = await ProblemDocument.SignatureAsync(await SendAsync(client, probe, Guid.NewGuid()));

                Assert.True(foreign == random, $"{probe.Method} {probe.Suffix}: a foreign account answered {foreign}, a random one {random}.");
                Assert.Equal((HttpStatusCode.NotFound, "AccountNotFound"), foreign);
            }
        }

        var stored = await world.AccountsAsync(async accounts => { return await accounts.Accounts.AsNoTracking().ToListAsync(); });
        Assert.Equal(3, stored.Count);
        foreach (var (id, planId) in new[] { (stranger.AccountId, stranger.PlanId), (world.HouseAccountId, world.HousePlanId) })
        {
            var account = stored.Single(candidate => { return candidate.Id == id; });
            Assert.Equal(AccountStatus.Active, account.Status);
            Assert.Equal(planId, account.PlanId);
            Assert.NotEqual("probe@example.com", account.OwnerEmail);
        }

        var refusals = await world.AuditEntriesAsync(
            AccountAuditActions.AccountSuspended,
            AccountAuditActions.AccountReactivated,
            AccountAuditActions.AccountPlanChanged,
            AccountAuditActions.AccountOwnerEmailChanged,
            AccountAuditActions.AccountDeleted);

        // Five mutations, two targets, each sent at the foreign row and at a random identifier.
        Assert.Equal(20, refusals.Count);
        Assert.All(refusals, entry =>
        {
            Assert.Equal(actor, entry.ActorUsername);
            Assert.False(entry.Succeeded);
        });
    }

    /// <summary>
    /// Every plan endpoint that names a plan answers a reseller that names another reseller's plan, or the
    /// administrator's, exactly as it answers a random identifier — 404 <c>PlanNotFound</c> — and changes
    /// nothing; every refused edit and deletion is journalled with the reseller as actor.
    /// </summary>
    /// <param name="actor">The reseller that probes.</param>
    [Theory]
    [MemberData(nameof(ActingResellers))]
    public async Task A_reseller_probing_a_plan_it_does_not_own_is_answered_as_a_random_identifier_is(string actor)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        var self = world.Reseller(actor);
        var stranger = world.Reseller(actor == "alpha" ? "bravo" : "alpha");
        using var client = await world.SignInAsync(actor);
        var probes = new (HttpMethod Method, object? Body)[]
        {
            (HttpMethod.Get, null),
            (HttpMethod.Put, ResellerRequestBodies.Plan("Renamed")),
            (HttpMethod.Delete, null),
        };

        using var own = await client.GetAsync($"/api/v1/plans/{self.PlanId}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);

        foreach (var target in new[] { stranger.PlanId, world.HousePlanId })
        {
            foreach (var (method, body) in probes)
            {
                var foreign = await ProblemDocument.SignatureAsync(await SendPlanAsync(client, method, body, target));
                var random = await ProblemDocument.SignatureAsync(await SendPlanAsync(client, method, body, Guid.NewGuid()));

                Assert.True(foreign == random, $"{method}: a foreign plan answered {foreign}, a random one {random}.");
                Assert.Equal((HttpStatusCode.NotFound, "PlanNotFound"), foreign);
            }
        }

        var names = await world.AccountsAsync(async accounts =>
        {
            return await accounts.Plans.AsNoTracking().Select(plan => plan.DisplayNameKey).ToListAsync();
        });
        Assert.Equal(["AlphaPlan", "BravoPlan", ResellerWorld.HousePlanName], names.Order());
        var refusals = await world.AuditEntriesAsync(AccountAuditActions.PlanUpdated, AccountAuditActions.PlanDeleted);
        Assert.Equal(8, refusals.Count);
        Assert.All(refusals, entry =>
        {
            Assert.Equal(actor, entry.ActorUsername);
            Assert.False(entry.Succeeded);
        });
    }

    /// <summary>
    /// A reseller that puts a new account on another reseller's plan, the administrator's plan or a plan
    /// that does not exist, or moves its own account onto one, is answered 404 <c>PlanNotFound</c> in all
    /// three cases: nothing is created, nothing moves, the host is never asked, and each refusal is
    /// journalled.
    /// </summary>
    /// <param name="actor">The reseller that probes.</param>
    [Theory]
    [MemberData(nameof(ActingResellers))]
    public async Task A_reseller_cannot_put_an_account_on_a_plan_it_does_not_own(string actor)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        var self = world.Reseller(actor);
        var stranger = world.Reseller(actor == "alpha" ? "bravo" : "alpha");
        using var client = await world.SignInAsync(actor);

        foreach (var plan in new[] { stranger.PlanId, world.HousePlanId, Guid.NewGuid() })
        {
            var created = await client.PostAsJsonAsync("/api/v1/accounts", ResellerRequestBodies.Account("probe", plan));
            var moved = await client.PutAsJsonAsync($"/api/v1/accounts/{self.AccountId}/plan", new { PlanId = plan });

            Assert.Equal((HttpStatusCode.NotFound, "PlanNotFound"), await ProblemDocument.SignatureAsync(created));
            Assert.Equal((HttpStatusCode.NotFound, "PlanNotFound"), await ProblemDocument.SignatureAsync(moved));
        }

        Assert.Equal(3, await world.AccountsAsync(async accounts => { return await accounts.Accounts.CountAsync(); }));
        Assert.Equal(self.PlanId, await world.AccountsAsync(async accounts =>
        {
            return await accounts.Accounts.Where(account => account.Id == self.AccountId)
                .Select(account => account.PlanId)
                .SingleAsync();
        }));
        Assert.Empty(world.Agent.Created);
        Assert.Empty(world.Agent.Quotas);
        foreach (var action in new[] { AccountAuditActions.AccountCreated, AccountAuditActions.AccountPlanChanged })
        {
            var refusals = await world.AuditEntriesAsync(action);
            Assert.Equal(3, refusals.Count);
            Assert.All(refusals, entry =>
            {
                Assert.Equal(actor, entry.ActorUsername);
                Assert.False(entry.Succeeded);
            });
        }
    }

    /// <summary>
    /// Every list a reseller reads — accounts, account choices, plans, plan choices — holds its own rows
    /// and none of another tenant's, however the list is searched or filtered, and the reseller's own
    /// standing names itself.
    /// </summary>
    /// <param name="actor">The reseller that reads.</param>
    [Theory]
    [MemberData(nameof(ActingResellers))]
    public async Task Every_list_a_reseller_reads_holds_its_own_rows_and_none_of_another_tenants(string actor)
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        var self = world.Reseller(actor);
        var stranger = world.Reseller(actor == "alpha" ? "bravo" : "alpha");
        using var client = await world.SignInAsync(actor);

        // Equality with exactly the reseller's own rows is the positive control: a list that returned
        // nothing at all would fail it.
        Assert.Equal([self.AccountName], await NamesAsync(client, "/api/v1/accounts", "name"));
        Assert.Equal([self.AccountName], await NamesAsync(client, "/api/v1/accounts/choices", "name"));
        Assert.Equal([self.PlanName], await NamesAsync(client, "/api/v1/plans", "displayName"));
        Assert.Equal([self.PlanName], await NamesAsync(client, "/api/v1/plans/choices", "displayName"));

        Assert.Empty(await NamesAsync(client, $"/api/v1/accounts?search={stranger.AccountName}", "name"));
        Assert.Empty(await NamesAsync(client, $"/api/v1/accounts?resellerId={stranger.ResellerId}", "name"));
        Assert.Empty(await NamesAsync(client, $"/api/v1/accounts/choices?search={stranger.AccountName}", "name"));
        Assert.Empty(await NamesAsync(client, $"/api/v1/accounts?search={ResellerWorld.HouseAccountName}", "name"));
        Assert.Empty(await NamesAsync(client, $"/api/v1/plans?resellerId={stranger.ResellerId}", "displayName"));
        Assert.Empty(await NamesAsync(client, $"/api/v1/plans?search={ResellerWorld.HousePlanName}", "displayName"));
        Assert.Empty(await NamesAsync(client, $"/api/v1/plans/choices?search={stranger.PlanName}", "displayName"));

        using var me = JsonDocument.Parse(await client.GetStringAsync("/api/v1/reseller/me"));
        Assert.Equal(self.ResellerId, me.RootElement.GetProperty("id").GetGuid());
    }

    /// <summary>
    /// The administrator sees every account and every plan with the reseller that owns it named, can
    /// narrow either list to one reseller, and reads any single row; the administrator's own rows name no
    /// reseller. This is the inverse control of the isolation above: the rows a reseller cannot see exist
    /// and are visible to the one caller that may see them.
    /// </summary>
    [Fact]
    public async Task The_administrator_sees_every_account_and_plan_with_its_owner_and_can_filter_by_reseller()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        Assert.Equal(
            ["alphaone", "bravoone", "house"],
            (await NamesAsync(admin, "/api/v1/accounts", "name")).Order());
        Assert.Equal(
            ["alphaone", "bravoone", "house"],
            (await NamesAsync(admin, "/api/v1/accounts/choices", "name")).Order());
        Assert.Equal(
            ["AlphaPlan", "BravoPlan", "HousePlan"],
            (await NamesAsync(admin, "/api/v1/plans", "displayName")).Order());
        Assert.Equal(
            ["AlphaPlan", "BravoPlan", "HousePlan"],
            (await NamesAsync(admin, "/api/v1/plans/choices", "displayName")).Order());
        Assert.Equal(["alphaone"], await NamesAsync(admin, $"/api/v1/accounts?resellerId={world.Alpha.ResellerId}", "name"));
        Assert.Equal(["BravoPlan"], await NamesAsync(admin, $"/api/v1/plans?resellerId={world.Bravo.ResellerId}", "displayName"));

        Assert.Equal(
            [("alphaone", "alpha"), ("bravoone", "bravo"), ("house", null)],
            (await OwnersAsync(admin, "/api/v1/accounts", "name")).Order());
        Assert.Equal(
            [("AlphaPlan", "alpha"), ("BravoPlan", "bravo"), ("HousePlan", null)],
            (await OwnersAsync(admin, "/api/v1/plans", "displayName")).Order());

        using var account = JsonDocument.Parse(await admin.GetStringAsync($"/api/v1/accounts/{world.Bravo.AccountId}"));
        Assert.Equal(world.Bravo.ResellerId, account.RootElement.GetProperty("resellerId").GetGuid());
        Assert.Equal("bravo", account.RootElement.GetProperty("resellerName").GetString());
        using var plan = JsonDocument.Parse(await admin.GetStringAsync($"/api/v1/plans/{world.Alpha.PlanId}"));
        Assert.Equal("alpha", plan.RootElement.GetProperty("resellerName").GetString());
    }

    /// <summary>
    /// The administrator reads any account's resource summary, with every module's counters answering;
    /// a reseller reads its own and is told 404 for another's. The administrator's read is the control
    /// that shows the 404 is the scope and not a route that fails.
    /// </summary>
    [Fact]
    public async Task The_resource_summary_is_the_administrators_for_any_account_and_a_resellers_for_its_own_only()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).WithEveryModuleSchema().BuildAsync();
        using var admin = await world.SignInAsync("admin");
        using var alpha = await world.SignInAsync("alpha");

        var byAdmin = await admin.GetAsync($"/api/v1/accounts/{world.Bravo.AccountId}/resource-summary");
        var ownByReseller = await alpha.GetAsync($"/api/v1/accounts/{world.Alpha.AccountId}/resource-summary");
        var foreignByReseller = await alpha.GetAsync($"/api/v1/accounts/{world.Bravo.AccountId}/resource-summary");

        Assert.Equal(HttpStatusCode.OK, byAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownByReseller.StatusCode);
        Assert.Equal((HttpStatusCode.NotFound, "AccountNotFound"), await ProblemDocument.SignatureAsync(foreignByReseller));
    }

    /// <summary>Sends one account probe at one account identifier.</summary>
    /// <param name="client">The prober's client.</param>
    /// <param name="probe">The probe.</param>
    /// <param name="accountId">The identifier to put in the route.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, AccountProbe probe, Guid accountId)
    {
        using var request = new HttpRequestMessage(probe.Method, $"/api/v1/accounts/{accountId}{probe.Suffix}");
        if (probe.Body is not null)
        {
            request.Content = JsonContent.Create(probe.Body);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Sends one plan probe at one plan identifier.</summary>
    /// <param name="client">The prober's client.</param>
    /// <param name="method">The HTTP verb.</param>
    /// <param name="body">The request body, or <c>null</c> for none.</param>
    /// <param name="planId">The identifier to put in the route.</param>
    /// <returns>The response.</returns>
    private static async Task<HttpResponseMessage> SendPlanAsync(
        HttpClient client,
        HttpMethod method,
        object? body,
        Guid planId)
    {
        using var request = new HttpRequestMessage(method, $"/api/v1/plans/{planId}");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Reads one text member of every row a list answers.</summary>
    /// <param name="client">The reader's client.</param>
    /// <param name="url">The list route, with its query string.</param>
    /// <param name="member">The row member to read.</param>
    /// <returns>The values, in the order the list answered them.</returns>
    private static async Task<IReadOnlyList<string>> NamesAsync(HttpClient client, string url, string member)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync(url));
        return body.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(row =>
            {
                return row.GetProperty(member).GetString()!;
            })
            .ToList();
    }

    /// <summary>Reads each row's name beside the name of the reseller that owns it.</summary>
    /// <param name="client">The reader's client.</param>
    /// <param name="url">The list route.</param>
    /// <param name="member">The row member holding the row's own name.</param>
    /// <returns>Name and owner pairs; the owner is <c>null</c> for an administrator's row.</returns>
    private static async Task<IReadOnlyList<(string Name, string? Owner)>> OwnersAsync(
        HttpClient client,
        string url,
        string member)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync(url));
        return body.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(row =>
            {
                return (row.GetProperty(member).GetString()!, row.GetProperty("resellerName").GetString());
            })
            .ToList();
    }

    /// <summary>One request an account endpoint is probed with, minus the account it is aimed at.</summary>
    /// <param name="Method">The HTTP verb.</param>
    /// <param name="Suffix">The route after the account identifier.</param>
    /// <param name="Body">The request body, or <c>null</c> for none.</param>
    private sealed record AccountProbe(HttpMethod Method, string Suffix, object? Body);
}
