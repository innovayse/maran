using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The reseller's audit screen (issue #370, <c>GET /api/v1/audit/reseller</c>) over real HTTP against
/// a real PostgreSQL: a reseller reads the entries about its own accounts and its own login and
/// nothing else, an account it does not own answers exactly as one that does not exist, and the two
/// roles each keep to their own route.
/// </summary>
/// <remarks>
/// Every seeded entry carries the action <c>SiteCreated</c> and every read filters by it, because a
/// sign-in writes entries of its own (the reseller is its own login's actor) and would otherwise
/// pollute the count. The positive controls are the reseller reading its OWN entries and the
/// administrator reading ALL of them over the same journal, so a route that answered nothing, or
/// everything, fails.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ResellerAuditEndpointTests : IAsyncLifetime
{
    /// <summary>The instant the seeded entries count from, well before any sign-in the test makes.</summary>
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerAuditEndpointTests(PostgresFixture postgres)
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
    /// Each reseller reads the entries of its own account and its own login, and neither the other
    /// reseller's, the administrator's, nor an unstamped server-wide entry.
    /// </summary>
    [Fact]
    public async Task A_reseller_reads_its_own_accounts_and_login_and_nothing_else()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        var alphaAccount = Entry("alpha-customer", "alpha account", Epoch, world.Alpha.AccountId);
        var alphaLogin = Entry("alpha", "alpha login", Epoch.AddMinutes(1), null, world.Alpha.UserId);
        var bravoAccount = Entry("bravo-customer", "bravo account", Epoch, world.Bravo.AccountId);
        var houseAccount = Entry("house-customer", "house account", Epoch, world.HouseAccountId);
        var serverWide = Entry("admin", "server wide", Epoch);
        await SeedAsync(world, alphaAccount, alphaLogin, bravoAccount, houseAccount, serverWide);

        using var alpha = await world.SignInAsync("alpha");
        using var bravo = await world.SignInAsync("bravo");
        using var admin = await world.SignInAsync("admin");

        using var alphaRead = await GetJsonAsync(alpha, "/api/v1/audit/reseller?action=SiteCreated");
        using var bravoRead = await GetJsonAsync(bravo, "/api/v1/audit/reseller?action=SiteCreated");
        using var adminRead = await GetJsonAsync(admin, "/api/v1/audit?action=SiteCreated");

        Assert.Equal([alphaLogin.Id, alphaAccount.Id], Ids(alphaRead));
        Assert.Equal([bravoAccount.Id], Ids(bravoRead));
        Assert.Equal(5, adminRead.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// Filtering by one of the reseller's own accounts works, and by another reseller's, the
    /// administrator's or a random one answers the same 404 with the same code.
    /// </summary>
    [Fact]
    public async Task An_account_the_reseller_does_not_own_is_answered_as_a_random_identifier_is()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        var own = Entry("alpha-customer", "alpha account", Epoch, world.Alpha.AccountId);
        await SeedAsync(world, own, Entry("bravo-customer", "bravo account", Epoch, world.Bravo.AccountId));
        using var alpha = await world.SignInAsync("alpha");

        using var owned = await GetJsonAsync(alpha, $"/api/v1/audit/reseller?account={world.Alpha.AccountId}");
        var random = await ProblemDocument.SignatureAsync(
            await alpha.GetAsync($"/api/v1/audit/reseller?account={Guid.NewGuid()}"));

        Assert.Equal([own.Id], Ids(owned));
        Assert.Equal(HttpStatusCode.NotFound, random.Status);
        foreach (var foreign in new[] { world.Bravo.AccountId, world.HouseAccountId })
        {
            var signature = await ProblemDocument.SignatureAsync(
                await alpha.GetAsync($"/api/v1/audit/reseller?account={foreign}"));

            Assert.Equal(random, signature);
        }
    }

    /// <summary>The action filter offers a reseller only the actions of its own part of the journal.</summary>
    [Fact]
    public async Task The_action_choices_hold_only_the_actions_of_the_resellers_own_part()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        await SeedAsync(
            world,
            Entry("alpha-customer", "own", Epoch, world.Alpha.AccountId, action: "OwnOnlyAction"),
            Entry("bravo-customer", "foreign", Epoch, world.Bravo.AccountId, action: "ForeignOnlyAction"));
        using var alpha = await world.SignInAsync("alpha");

        using var choices = await GetJsonAsync(alpha, "/api/v1/audit/reseller/actions");
        var actions = choices.RootElement.EnumerateArray()
            .Select(choice => { return choice.GetProperty("action").GetString(); })
            .ToList();

        Assert.Contains("OwnOnlyAction", actions);
        Assert.DoesNotContain("ForeignOnlyAction", actions);
    }

    /// <summary>
    /// The two routes keep to their roles: the administrator's journal refuses a reseller, and the
    /// reseller's refuses an administrator, each with a 403 — the inverse control being each role
    /// reading its own route.
    /// </summary>
    [Fact]
    public async Task Each_role_is_refused_the_other_roles_route()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var admin = await world.SignInAsync("admin");

        Assert.Equal(HttpStatusCode.OK, (await alpha.GetAsync("/api/v1/audit/reseller")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alpha.GetAsync("/api/v1/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await alpha.GetAsync("/api/v1/audit/actions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/audit/reseller")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/audit/reseller/actions")).StatusCode);
    }

    /// <summary>Writes entries into the journal.</summary>
    /// <param name="world">The booted panel.</param>
    /// <param name="entries">The entries to add.</param>
    /// <returns>Resolves once they are saved.</returns>
    private static async Task SeedAsync(ResellerWorld world, params AuditEvent[] entries)
    {
        await world.IdentityAsync(async identity =>
        {
            identity.AuditEvents.AddRange(entries);
            await identity.SaveChangesAsync();
            return 0;
        });
    }

    /// <summary>Builds one journal entry.</summary>
    /// <param name="actor">The name the actor used.</param>
    /// <param name="subject">What it was attempted on.</param>
    /// <param name="at">When it happened.</param>
    /// <param name="accountId">The account the entry is stamped with, or <c>null</c> for none.</param>
    /// <param name="actorUserId">The user who did it, or <c>null</c> for none.</param>
    /// <param name="action">What was attempted.</param>
    private static AuditEvent Entry(
        string actor,
        string subject,
        DateTimeOffset at,
        Guid? accountId = null,
        Guid? actorUserId = null,
        string action = "SiteCreated")
    {
        return new AuditEvent(
            Guid.NewGuid(), at, actorUserId, actor, action, subject, "192.0.2.1", "test", true, null, null, accountId);
    }

    /// <summary>Reads a successful JSON answer.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="path">The path and query string.</param>
    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>The ids on a page, in order.</summary>
    /// <param name="page">The page's JSON.</param>
    private static List<Guid> Ids(JsonDocument page)
    {
        return page.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => { return item.GetProperty("id").GetGuid(); })
            .ToList();
    }
}
