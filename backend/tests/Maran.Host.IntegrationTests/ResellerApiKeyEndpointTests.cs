using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Reseller API keys over real HTTP against a real PostgreSQL (issue #368): a reseller mints keys that
/// belong to it, an integration reads that reseller's own plans and accounts with one, and nothing
/// reaches another reseller's rows, the administrator's, or the key-management surface.
/// </summary>
/// <remarks>
/// <para>
/// Every refusal has an accepting control beside it: the same call with a key that is allowed answers
/// 200, so a route that refused every key could not pass. The IDOR probes are sent at a foreign row and
/// at a random identifier and compared, as in <see cref="ResellerIsolationTests"/>.
/// </para>
/// <para>
/// UNOBSERVED HERE: address binding of a reseller's key (the same authenticator as the administrator's,
/// covered by <see cref="ApiKeyAuthenticationTests"/>), and the exhaustion of the per-reseller ceiling
/// (covered by the handler's unit tests).
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ResellerApiKeyEndpointTests : IAsyncLifetime
{
    /// <summary>The reseller key management route.</summary>
    private const string KeysRoute = "/api/v1/reseller/api-keys";

    /// <summary>The route a key reads the accounts of its reseller at.</summary>
    private const string AccountsRoute = "/api/v1/reseller-api/accounts";

    /// <summary>The route a key reads the plans of its reseller at.</summary>
    private const string PlansRoute = "/api/v1/reseller-api/plans";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ResellerApiKeyEndpointTests(PostgresFixture postgres)
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

    /// <summary>Creates a key as a signed-in reseller and returns its id and plaintext.</summary>
    private static async Task<(Guid Id, string Secret)> CreateKeyAsync(
        HttpClient reseller,
        string name,
        params string[] scopes)
    {
        var response = await reseller.PostAsJsonAsync(KeysRoute, new { Name = name, Scopes = scopes });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (body.RootElement.GetProperty("key").GetProperty("id").GetGuid(), body.RootElement.GetProperty("secret").GetString()!);
    }

    /// <summary>Sends a GET carrying only the key header, as an integration does.</summary>
    private static async Task<HttpResponseMessage> GetWithKeyAsync(ResellerWorld world, string path, string key)
    {
        using var client = world.Factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Api-Key", key);
        return await client.SendAsync(request);
    }

    /// <summary>Reads the ids on the <c>items</c> of a page.</summary>
    private static async Task<List<Guid>> IdsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("items").EnumerateArray().Select(item => { return item.GetProperty("id").GetGuid(); }).ToList();
    }

    /// <summary>A reseller's key reads that reseller's accounts and plans and no one else's.</summary>
    [Fact]
    public async Task A_resellers_key_lists_its_own_accounts_and_plans_and_no_others()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        var (_, secret) = await CreateKeyAsync(alpha, "provisioning", ApiKeyScopes.AccountsRead, ApiKeyScopes.PlansRead);

        var accounts = await IdsAsync(await GetWithKeyAsync(world, AccountsRoute, secret));
        var plans = await IdsAsync(await GetWithKeyAsync(world, PlansRoute, secret));

        Assert.Equal([world.Alpha.AccountId], accounts);
        Assert.Equal([world.Alpha.PlanId], plans);
        Assert.DoesNotContain(world.Bravo.AccountId, accounts);
        Assert.DoesNotContain(world.HouseAccountId, accounts);
        Assert.DoesNotContain(world.HousePlanId, plans);
    }

    /// <summary>Each reseller's key sees its own rows: the control that the narrowing is by owner and not a fixed answer.</summary>
    [Fact]
    public async Task Two_resellers_keys_see_disjoint_accounts()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var bravo = await world.SignInAsync("bravo");
        var (_, alphaKey) = await CreateKeyAsync(alpha, "a", ApiKeyScopes.AccountsRead);
        var (_, bravoKey) = await CreateKeyAsync(bravo, "b", ApiKeyScopes.AccountsRead);

        Assert.Equal([world.Alpha.AccountId], await IdsAsync(await GetWithKeyAsync(world, AccountsRoute, alphaKey)));
        Assert.Equal([world.Bravo.AccountId], await IdsAsync(await GetWithKeyAsync(world, AccountsRoute, bravoKey)));
    }

    /// <summary>A plan of another reseller, or the administrator's, answers a key exactly as a random identifier does.</summary>
    [Fact]
    public async Task A_key_asking_for_a_plan_it_does_not_own_is_answered_as_a_random_identifier_is()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        var (_, secret) = await CreateKeyAsync(alpha, "plans", ApiKeyScopes.PlansRead);

        Assert.Equal(HttpStatusCode.OK, (await GetWithKeyAsync(world, $"{PlansRoute}/{world.Alpha.PlanId}", secret)).StatusCode);
        var random = await ProblemDocument.SignatureAsync(await GetWithKeyAsync(world, $"{PlansRoute}/{Guid.NewGuid()}", secret));

        foreach (var foreign in new[] { world.Bravo.PlanId, world.HousePlanId })
        {
            var signature = await ProblemDocument.SignatureAsync(await GetWithKeyAsync(world, $"{PlansRoute}/{foreign}", secret));
            Assert.Equal(random, signature);
            Assert.Equal(HttpStatusCode.NotFound, signature.Status);
        }
    }

    /// <summary>A key holding one reseller scope is refused the endpoint of the other, and a key with neither is refused both.</summary>
    [Fact]
    public async Task A_key_is_refused_the_endpoint_of_a_scope_it_does_not_hold()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        var (_, plansOnly) = await CreateKeyAsync(alpha, "plans", ApiKeyScopes.PlansRead);
        var (_, selfOnly) = await CreateKeyAsync(alpha, "self", ApiKeyScopes.SelfRead);

        Assert.Equal(HttpStatusCode.OK, (await GetWithKeyAsync(world, PlansRoute, plansOnly)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetWithKeyAsync(world, AccountsRoute, plansOnly)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetWithKeyAsync(world, PlansRoute, selfOnly)).StatusCode);
    }

    /// <summary>The administrator's server key is refused the reseller endpoints, and a reseller's key is refused the server status.</summary>
    [Fact]
    public async Task A_key_of_one_owner_is_refused_the_endpoints_of_the_other()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");
        var created = await admin.PostAsJsonAsync("/api/v1/api-keys", new { Name = "server", Scopes = new[] { ApiKeyScopes.ServerRead } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var administratorKey = body.RootElement.GetProperty("secret").GetString()!;
        using var alpha = await world.SignInAsync("alpha");
        var (_, resellerKey) = await CreateKeyAsync(alpha, "accounts", ApiKeyScopes.AccountsRead);

        Assert.Equal(HttpStatusCode.OK, (await GetWithKeyAsync(world, "/api/v1/api-keys/self", administratorKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetWithKeyAsync(world, AccountsRoute, administratorKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetWithKeyAsync(world, "/api/v1/server/status", resellerKey)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetWithKeyAsync(world, AccountsRoute, resellerKey)).StatusCode);
    }

    /// <summary>A scope of the wrong grade is refused with a 400 at creation, on both sides.</summary>
    [Fact]
    public async Task A_scope_of_the_other_owners_grade_is_refused_at_creation()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var admin = await world.SignInAsync("admin");

        var reseller = await alpha.PostAsJsonAsync(KeysRoute, new { Name = "x", Scopes = new[] { ApiKeyScopes.ServerRead } });
        var administrator = await admin.PostAsJsonAsync("/api/v1/api-keys", new { Name = "x", Scopes = new[] { ApiKeyScopes.AccountsRead } });

        Assert.Equal(HttpStatusCode.BadRequest, reseller.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, administrator.StatusCode);
        Assert.Equal("ApiKeyScopeUnknown", await ProblemDocument.CodeAsync(reseller));
        Assert.Equal("ApiKeyScopeUnknown", await ProblemDocument.CodeAsync(administrator));
    }

    /// <summary>A reseller lists only its own keys, the administrator's list holds none of them, and the owner is the reseller's id.</summary>
    [Fact]
    public async Task Each_owner_lists_only_its_own_keys()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var bravo = await world.SignInAsync("bravo");
        using var admin = await world.SignInAsync("admin");
        var (alphaKeyId, _) = await CreateKeyAsync(alpha, "alpha-key", ApiKeyScopes.AccountsRead);
        await CreateKeyAsync(bravo, "bravo-key", ApiKeyScopes.AccountsRead);

        Assert.Equal([alphaKeyId], await IdsAsync(await alpha.GetAsync(KeysRoute)));
        Assert.Empty(await IdsAsync(await admin.GetAsync("/api/v1/api-keys")));
        var stored = await world.IdentityAsync(async identity => { return await identity.ApiKeys.AsNoTracking().SingleAsync(key => key.Id == alphaKeyId); });
        Assert.Equal(world.Alpha.ResellerId, stored.ResellerId);
    }

    /// <summary>A reseller revoking another reseller's key, or the administrator revoking a reseller's, is answered 404 and changes nothing.</summary>
    [Fact]
    public async Task A_key_another_owner_holds_cannot_be_revoked()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var bravo = await world.SignInAsync("bravo");
        using var admin = await world.SignInAsync("admin");
        var (alphaKeyId, alphaSecret) = await CreateKeyAsync(alpha, "alpha-key", ApiKeyScopes.AccountsRead);

        var foreign = await ProblemDocument.SignatureAsync(await bravo.DeleteAsync($"{KeysRoute}/{alphaKeyId}"));
        var random = await ProblemDocument.SignatureAsync(await bravo.DeleteAsync($"{KeysRoute}/{Guid.NewGuid()}"));
        var byAdministrator = await ProblemDocument.SignatureAsync(await admin.DeleteAsync($"/api/v1/api-keys/{alphaKeyId}"));

        Assert.Equal((HttpStatusCode.NotFound, "ApiKeyNotFound"), foreign);
        Assert.Equal(random, foreign);
        Assert.Equal(foreign, byAdministrator);
        Assert.Equal(HttpStatusCode.OK, (await GetWithKeyAsync(world, AccountsRoute, alphaSecret)).StatusCode);
    }

    /// <summary>The owner revokes its key, and it authenticates nothing from the next call, with the one bodyless 401.</summary>
    [Fact]
    public async Task A_revoked_key_is_refused_with_the_same_401_as_any_other_refusal()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        var (id, secret) = await CreateKeyAsync(alpha, "to-revoke", ApiKeyScopes.AccountsRead);
        Assert.Equal(HttpStatusCode.OK, (await GetWithKeyAsync(world, AccountsRoute, secret)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await alpha.DeleteAsync($"{KeysRoute}/{id}")).StatusCode);
        var revoked = await GetWithKeyAsync(world, AccountsRoute, secret);

        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal(string.Empty, await revoked.Content.ReadAsStringAsync());
    }

    /// <summary>Suspending the reseller stops its keys on the next call, and resuming it lets them work again.</summary>
    [Fact]
    public async Task Suspending_the_reseller_stops_its_keys_and_resuming_it_restores_them()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var admin = await world.SignInAsync("admin");
        var (_, secret) = await CreateKeyAsync(alpha, "provisioning", ApiKeyScopes.AccountsRead);
        Assert.Equal(HttpStatusCode.OK, (await GetWithKeyAsync(world, AccountsRoute, secret)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/resellers/{world.Alpha.ResellerId}/suspend", null)).StatusCode);
        var suspended = await GetWithKeyAsync(world, AccountsRoute, secret);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/resellers/{world.Alpha.ResellerId}/reactivate", null)).StatusCode);
        var resumed = await GetWithKeyAsync(world, AccountsRoute, secret);

        Assert.Equal(HttpStatusCode.Unauthorized, suspended.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
    }

    /// <summary>A reseller's key cannot reach the key-management surface, an account's detail, the panel's own plan list or the reseller's own screen.</summary>
    [Fact]
    public async Task A_resellers_key_cannot_manage_keys_or_reach_the_login_endpoints()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        var (_, secret) = await CreateKeyAsync(alpha, "everything", ApiKeyScopes.AccountsRead, ApiKeyScopes.PlansRead);

        foreach (var path in new[] { KeysRoute, $"/api/v1/accounts/{world.Alpha.AccountId}", "/api/v1/plans", "/api/v1/reseller/me" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await GetWithKeyAsync(world, path, secret)).StatusCode);
        }

        using var client = world.Factory.CreateClient();
        var post = new HttpRequestMessage(HttpMethod.Post, KeysRoute) { Content = JsonContent.Create(new { Name = "x", Scopes = new[] { ApiKeyScopes.AccountsRead } }) };
        post.Headers.Add("X-Api-Key", secret);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(post)).StatusCode);
    }

    /// <summary>The creation is journalled with the reseller's login as actor, the prefix and scopes, and never the key or its digest; each call with the key is journalled too.</summary>
    [Fact]
    public async Task Creating_and_using_a_key_are_journalled_without_the_key()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        var (id, secret) = await CreateKeyAsync(alpha, "audited", ApiKeyScopes.AccountsRead);
        await GetWithKeyAsync(world, AccountsRoute, secret);

        var created = await world.AuditEntriesAsync(IdentityAuditActions.ApiKeyCreated);
        var calls = await world.AuditEntriesAsync(IdentityAuditActions.ApiKeyCall);
        var stored = await world.IdentityAsync(async identity => { return await identity.ApiKeys.AsNoTracking().SingleAsync(key => key.Id == id); });

        var entry = Assert.Single(created);
        Assert.Equal(world.Alpha.UserId, entry.ActorUserId);
        Assert.Contains(stored.Prefix, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(stored.TokenHash, entry.Subject, StringComparison.Ordinal);
        var call = Assert.Single(calls);
        Assert.Contains("api/v1/reseller-api/accounts", call.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, call.Subject, StringComparison.Ordinal);
    }

    /// <summary>The reseller key surface is for a reseller login only: the administrator and an anonymous caller are refused.</summary>
    [Fact]
    public async Task The_reseller_key_surface_is_refused_to_the_administrator_and_to_anonymous_callers()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var admin = await world.SignInAsync("admin");
        using var anonymous = world.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(KeysRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(KeysRoute)).StatusCode);
    }
}
