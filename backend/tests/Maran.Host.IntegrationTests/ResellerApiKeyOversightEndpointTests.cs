using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The administrator's oversight of one reseller's API keys over real HTTP against a real PostgreSQL
/// (issue #435): list them, revoke one while the reseller stays active, with the revoke journalled
/// under the administrator, and nobody but an administrator may do either.
/// </summary>
/// <remarks>
/// Every refusal has an accepting control beside it: the same call by the administrator answers 200 or
/// 204, so a route that refused everybody could not pass. UNOBSERVED HERE: the page the SPA draws over
/// these routes (the Playwright specs hold it).
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ResellerApiKeyOversightEndpointTests : IAsyncLifetime
{
    /// <summary>The reseller's own key management route, used to mint the keys under oversight.</summary>
    private const string OwnKeysRoute = "/api/v1/reseller/api-keys";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerApiKeyOversightEndpointTests(PostgresFixture postgres)
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

    /// <summary>The oversight route of one reseller's keys.</summary>
    private static string OversightRoute(Guid resellerId)
    {
        return $"/api/v1/resellers/{resellerId}/api-keys";
    }

    /// <summary>Creates a key as a signed-in reseller and returns its id and plaintext.</summary>
    private static async Task<(Guid Id, string Secret)> CreateKeyAsync(HttpClient reseller, string name)
    {
        var response = await reseller.PostAsJsonAsync(OwnKeysRoute, new { Name = name, Scopes = new[] { ApiKeyScopes.AccountsRead } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (body.RootElement.GetProperty("key").GetProperty("id").GetGuid(), body.RootElement.GetProperty("secret").GetString()!);
    }

    /// <summary>Reads the ids on the <c>items</c> of a page.</summary>
    private static async Task<List<Guid>> IdsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("items").EnumerateArray().Select(item => { return item.GetProperty("id").GetGuid(); }).ToList();
    }

    /// <summary>The administrator sees the keys of the reseller named in the route and no other reseller's, and not its own.</summary>
    [Fact]
    public async Task The_administrator_lists_the_keys_of_the_reseller_the_route_names()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var bravo = await world.SignInAsync("bravo");
        using var admin = await world.SignInAsync("admin");
        var (alphaKeyId, _) = await CreateKeyAsync(alpha, "alpha-key");
        var (bravoKeyId, _) = await CreateKeyAsync(bravo, "bravo-key");

        Assert.Equal([alphaKeyId], await IdsAsync(await admin.GetAsync(OversightRoute(world.Alpha.ResellerId))));
        Assert.Equal([bravoKeyId], await IdsAsync(await admin.GetAsync(OversightRoute(world.Bravo.ResellerId))));
    }

    /// <summary>The list is paged, searched and filtered by the panel like the other key lists.</summary>
    [Fact]
    public async Task The_oversight_list_is_searched_and_narrowed_by_status_on_the_server()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var admin = await world.SignInAsync("admin");
        var (billingId, _) = await CreateKeyAsync(alpha, "billing");
        var (reportsId, _) = await CreateKeyAsync(alpha, "reports");
        Assert.Equal(HttpStatusCode.NoContent, (await alpha.DeleteAsync($"{OwnKeysRoute}/{reportsId}")).StatusCode);

        Assert.Equal([billingId], await IdsAsync(await admin.GetAsync($"{OversightRoute(world.Alpha.ResellerId)}?search=bill")));
        Assert.Equal([reportsId], await IdsAsync(await admin.GetAsync($"{OversightRoute(world.Alpha.ResellerId)}?status=revoked")));
        Assert.Equal([billingId], await IdsAsync(await admin.GetAsync($"{OversightRoute(world.Alpha.ResellerId)}?status=active")));
    }

    /// <summary>A reseller that does not exist lists nothing, and no key is revealed by guessing an id.</summary>
    [Fact]
    public async Task A_reseller_that_does_not_exist_lists_no_keys()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");

        Assert.Empty(await IdsAsync(await admin.GetAsync(OversightRoute(Guid.NewGuid()))));
    }

    /// <summary>
    /// The administrator revokes one key while the reseller stays active: the key is refused from then
    /// on, the reseller's other key keeps working, and the revoke is journalled with the administrator as actor.
    /// </summary>
    [Fact]
    public async Task The_administrator_revokes_one_key_and_the_revoke_is_journalled_under_the_administrator()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var admin = await world.SignInAsync("admin");
        var (revokedId, revokedSecret) = await CreateKeyAsync(alpha, "to-revoke");
        var (keptId, keptSecret) = await CreateKeyAsync(alpha, "to-keep");

        var response = await admin.DeleteAsync($"{OversightRoute(world.Alpha.ResellerId)}/{revokedId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAccountsAsync(world, revokedSecret)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAccountsAsync(world, keptSecret)).StatusCode);
        var stored = await world.IdentityAsync(async identity => { return await identity.ApiKeys.AsNoTracking().SingleAsync(key => key.Id == keptId); });
        Assert.Null(stored.RevokedAt);
        var administratorId = await world.IdentityAsync(async identity =>
        {
            return await identity.Users.AsNoTracking().Where(user => user.Username == "admin").Select(user => user.Id).SingleAsync();
        });
        var entry = Assert.Single(await world.AuditEntriesAsync(IdentityAuditActions.ApiKeyRevoked));
        Assert.Equal(administratorId, entry.ActorUserId);
        Assert.DoesNotContain(revokedSecret, entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>Revoking through another reseller's route, or a random id, is answered 404 and changes nothing.</summary>
    [Fact]
    public async Task A_key_is_revoked_only_through_the_route_of_the_reseller_that_owns_it()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var admin = await world.SignInAsync("admin");
        var (alphaKeyId, alphaSecret) = await CreateKeyAsync(alpha, "alpha-key");

        var wrongReseller = await ProblemDocument.SignatureAsync(
            await admin.DeleteAsync($"{OversightRoute(world.Bravo.ResellerId)}/{alphaKeyId}"));
        var random = await ProblemDocument.SignatureAsync(
            await admin.DeleteAsync($"{OversightRoute(world.Alpha.ResellerId)}/{Guid.NewGuid()}"));

        Assert.Equal((HttpStatusCode.NotFound, "ApiKeyNotFound"), wrongReseller);
        Assert.Equal(random, wrongReseller);
        Assert.Equal(HttpStatusCode.OK, (await GetAccountsAsync(world, alphaSecret)).StatusCode);
    }

    /// <summary>Only an administrator may list or revoke a reseller's keys: a reseller (even the owner), and an anonymous caller, are refused.</summary>
    [Fact]
    public async Task Only_the_administrator_may_oversee_a_resellers_keys()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var bravo = await world.SignInAsync("bravo");
        using var anonymous = world.Factory.CreateClient();
        var (keyId, _) = await CreateKeyAsync(alpha, "alpha-key");
        var route = OversightRoute(world.Alpha.ResellerId);

        Assert.Equal(HttpStatusCode.Forbidden, (await alpha.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bravo.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bravo.DeleteAsync($"{route}/{keyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"{route}/{keyId}")).StatusCode);
        var stored = await world.IdentityAsync(async identity => { return await identity.ApiKeys.AsNoTracking().SingleAsync(key => key.Id == keyId); });
        Assert.Null(stored.RevokedAt);
    }

    /// <summary>Sends a GET to the reseller accounts route carrying only the key header, as an integration does.</summary>
    private static async Task<HttpResponseMessage> GetAccountsAsync(ResellerWorld world, string key)
    {
        using var client = world.Factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reseller-api/accounts");
        request.Headers.Add("X-Api-Key", key);
        return await client.SendAsync(request);
    }
}
