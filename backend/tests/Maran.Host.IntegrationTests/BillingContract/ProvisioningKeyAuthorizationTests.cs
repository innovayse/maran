using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests.BillingContract;

/// <summary>
/// Who may call the eight routes of the reseller API (issue #7): only a working key of a reseller that holds
/// the scope of the route. Every other caller, a missing, unknown, revoked or expired key, a key without the
/// scope, an administrator's key, a reseller's login, a key of a suspended reseller, is refused on every route
/// and nothing runs behind the refusal.
/// </summary>
/// <remarks>
/// <para>
/// A refusal that every caller got would pass all of these, so the accepting control is in the same class:
/// the right key succeeds on every route, in the same world. And "nothing runs" is asserted on what the
/// doubles saw (no account created or deleted on the host, no final backup asked for) and on the stored
/// account, not on the status alone.
/// </para>
/// <para>
/// UNOBSERVED HERE: the rate limit of the provisioning routes, and a key bound to an address (both have their
/// own tests); the expiry of a key is made by writing its stored expiry into the past, because the panel's
/// clock is not moved in this world.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ProvisioningKeyAuthorizationTests : IAsyncLifetime
{
    /// <summary>A well-formed key nobody was issued.</summary>
    private const string NeverIssued = "mk_0badc0de_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    /// <summary>The route a reseller revokes its keys at.</summary>
    private const string ResellerKeysRoute = "/api/v1/reseller/api-keys";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>One of the eight routes, with a request that would succeed for a caller allowed to make it.</summary>
    /// <param name="Name">The route in words, for a failure message.</param>
    /// <param name="Method">The HTTP method.</param>
    /// <param name="Path">The route relative to the reseller API's prefix.</param>
    /// <param name="Body">The JSON body, or <c>null</c> for none.</param>
    /// <param name="Scope">The scope that opens the route.</param>
    private sealed record Route(string Name, HttpMethod Method, string Path, object? Body, string Scope);

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ProvisioningKeyAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>The eight routes, aimed at alpha's own account and plans.</summary>
    private static IReadOnlyList<Route> RoutesOf(BillingContractWorld world)
    {
        var account = $"/accounts/{world.World.Alpha.AccountId}";

        return
        [
            new Route("server info", HttpMethod.Get, "/info", null, ApiKeyScopes.AccountsRead),
            new Route("read account", HttpMethod.Get, account, null, ApiKeyScopes.AccountsRead),
            new Route("read usage", HttpMethod.Get, account + "/usage", null, ApiKeyScopes.AccountsRead),
            new Route(
                "create",
                HttpMethod.Post,
                "/accounts",
                new
                {
                    Name = "authz1",
                    PrimaryDomain = "authz1.example.com",
                    PlanId = world.World.Alpha.PlanId,
                    OwnerEmail = "owner@example.com",
                },
                ApiKeyScopes.AccountsWrite),
            new Route("suspend", HttpMethod.Post, account + "/suspend", null, ApiKeyScopes.AccountsWrite),
            new Route("reactivate", HttpMethod.Post, account + "/reactivate", null, ApiKeyScopes.AccountsWrite),
            new Route("change plan", HttpMethod.Put, account + "/plan", new { PlanId = world.SmallPlanId }, ApiKeyScopes.AccountsWrite),
            new Route("terminate", HttpMethod.Delete, account, null, ApiKeyScopes.AccountsDelete),
        ];
    }

    /// <summary>Sends a route with a key, attaching a fresh <c>Idempotency-Key</c> to a write.</summary>
    private static Task<HttpResponseMessage> SendAsync(BillingContractWorld world, Route route, string? secret)
    {
        var key = route.Method == HttpMethod.Get ? null : "authz-" + Guid.NewGuid().ToString("N");

        return world.SendAsync(route.Method, route.Path, secret, key, route.Body);
    }

    /// <summary>Asserts a key is answered <paramref name="expected"/> on each of <paramref name="routes"/>.</summary>
    private static async Task AssertAnsweredAsync(
        BillingContractWorld world,
        IEnumerable<Route> routes,
        string? secret,
        HttpStatusCode expected,
        string who)
    {
        foreach (var route in routes)
        {
            using var response = await SendAsync(world, route, secret);

            Assert.True(
                response.StatusCode == expected,
                $"{who} on '{route.Name}': expected {(int)expected} but the panel answered {(int)response.StatusCode}.");

            if (expected == HttpStatusCode.Unauthorized)
            {
                Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
            }
        }
    }

    /// <summary>Asserts that no write reached the host or the stored account, whatever was refused.</summary>
    private static async Task AssertNothingRanAsync(BillingContractWorld world)
    {
        Assert.Empty(world.Agent.Created);
        Assert.Empty(world.Agent.Deleted);
        Assert.Empty(world.Backup.Creates);

        var stored = await world.StoredAccountAsync(world.World.Alpha.AccountId);
        Assert.NotNull(stored);
        Assert.Equal(world.World.Alpha.PlanId, stored.PlanId);
        Assert.Equal(Maran.Modules.Accounts.Domain.Enums.AccountStatus.Active, stored.Status);
    }

    /// <summary>Gives a key of alpha's an expiry in the past, as a clock moved past it would.</summary>
    private static async Task ExpireAsync(BillingContractWorld world, Guid keyId)
    {
        var past = (DateTimeOffset?)world.World.Factory.Services.GetRequiredService<IClock>().UtcNow.AddMinutes(-5);

        await world.World.IdentityAsync(async identity =>
        {
            // Set by name through the change tracker: the entity's setters are private by design, and a
            // bulk update would need a statement-bodied selector, which an expression tree cannot hold.
            var key = await identity.ApiKeys.SingleAsync(row => row.Id == keyId);
            identity.Entry(key).Property("ExpiresAt").CurrentValue = past;
            await identity.SaveChangesAsync();
            return 0;
        });
    }

    /// <summary>No key, a key nobody was issued, a revoked key and an expired key are the bodyless 401 on every route.</summary>
    [Fact]
    public async Task A_missing_unknown_revoked_or_expired_key_is_refused_with_the_bodyless_401_on_every_route()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);
        var routes = RoutesOf(world);
        var scopes = new[] { ApiKeyScopes.AccountsRead, ApiKeyScopes.AccountsWrite, ApiKeyScopes.AccountsDelete };
        var revoked = await BillingContractWorld.IssueKeyAsync(world.World, "alpha", "to-revoke", scopes);
        var expired = await BillingContractWorld.IssueKeyAsync(world.World, "alpha", "to-expire", scopes);
        using (var alpha = await world.World.SignInAsync("alpha"))
        {
            Assert.Equal(HttpStatusCode.NoContent, (await alpha.DeleteAsync($"{ResellerKeysRoute}/{revoked.Id}")).StatusCode);
        }

        await ExpireAsync(world, expired.Id);

        // The accepting control: the same world answers a working key, so a panel refusing everything fails here.
        using (var control = await world.SendAsync(HttpMethod.Get, "/info", world.Full.Secret))
        {
            Assert.Equal(HttpStatusCode.OK, control.StatusCode);
        }

        await AssertAnsweredAsync(world, routes, null, HttpStatusCode.Unauthorized, "no key");
        await AssertAnsweredAsync(world, routes, NeverIssued, HttpStatusCode.Unauthorized, "a key nobody was issued");
        await AssertAnsweredAsync(world, routes, revoked.Secret, HttpStatusCode.Unauthorized, "a revoked key");
        await AssertAnsweredAsync(world, routes, expired.Secret, HttpStatusCode.Unauthorized, "an expired key");
        await AssertNothingRanAsync(world);
    }

    /// <summary>A working key of a reseller is refused with 403 on each route whose scope it does not hold.</summary>
    [Fact]
    public async Task A_key_without_the_scope_of_a_route_is_refused_with_403_on_that_route()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);
        var routes = RoutesOf(world);
        var selfOnly = await BillingContractWorld.IssueKeyAsync(world.World, "alpha", "self", ApiKeyScopes.SelfRead);
        var deleteOnly = await BillingContractWorld.IssueKeyAsync(world.World, "alpha", "delete", ApiKeyScopes.AccountsDelete);

        await AssertAnsweredAsync(world, routes, selfOnly.Secret, HttpStatusCode.Forbidden, "a key with self.read only");
        await AssertAnsweredAsync(
            world,
            routes.Where(route => { return route.Scope != ApiKeyScopes.AccountsDelete; }),
            deleteOnly.Secret,
            HttpStatusCode.Forbidden,
            "a key with accounts.delete only");
        await AssertAnsweredAsync(
            world,
            routes.Where(route => { return route.Scope != ApiKeyScopes.AccountsRead; }),
            world.Reader.Secret,
            HttpStatusCode.Forbidden,
            "a key with accounts.read only");
        await AssertAnsweredAsync(
            world,
            routes.Where(route => { return route.Scope == ApiKeyScopes.AccountsDelete; }),
            world.Provisioner.Secret,
            HttpStatusCode.Forbidden,
            "a key with accounts.read and accounts.write");

        // The accepting control: a scope held opens the routes it names (the reads, which change nothing).
        await AssertAnsweredAsync(
            world,
            routes.Where(route => { return route.Scope == ApiKeyScopes.AccountsRead; }),
            world.Reader.Secret,
            HttpStatusCode.OK,
            "a key with accounts.read");
        await AssertNothingRanAsync(world);
    }

    /// <summary>
    /// A key the administrator owns is refused on every route even when its stored row names the reseller scopes,
    /// because the scope vocabulary is split by owner and the policy asks for the reseller as well.
    /// </summary>
    [Fact]
    public async Task An_administrators_key_holding_the_scope_strings_is_refused_with_403_on_every_route()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);
        using var admin = await world.World.SignInAsync("admin");
        var created = await admin.PostAsJsonAsync(
            "/api/v1/api-keys", new { Name = "server", Scopes = new[] { ApiKeyScopes.ServerRead } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("key").GetProperty("id").GetGuid();
        var secret = body.RootElement.GetProperty("secret").GetString()!;
        string[] forced = [ApiKeyScopes.AccountsRead, ApiKeyScopes.AccountsWrite, ApiKeyScopes.AccountsDelete];

        await world.World.IdentityAsync(async identity =>
        {
            // Set by name through the change tracker: see ExpireAsync.
            var key = await identity.ApiKeys.SingleAsync(row => row.Id == id);
            identity.Entry(key).Property("Scopes").CurrentValue = forced;
            await identity.SaveChangesAsync();
            return 0;
        });
        var stored = await world.World.IdentityAsync(async identity =>
        {
            return await identity.ApiKeys.AsNoTracking().SingleAsync(key => key.Id == id);
        });

        // The row really holds the strings and no reseller: otherwise the refusal below proves nothing.
        Assert.Null(stored.ResellerId);
        Assert.Contains(ApiKeyScopes.AccountsWrite, stored.Scopes);

        await AssertAnsweredAsync(world, RoutesOf(world), secret, HttpStatusCode.Forbidden, "an administrator's key");
        await AssertNothingRanAsync(world);
    }

    /// <summary>A reseller's login is refused on the key routes, and its key is refused on the login routes.</summary>
    [Fact]
    public async Task A_resellers_login_is_refused_on_the_key_routes_and_its_key_on_the_login_routes()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);
        using var alpha = await world.World.SignInAsync("alpha");
        var own = world.World.Alpha.AccountId;

        // The accepting controls: the login works on the panel's own route, the key on the reseller API's.
        Assert.Equal(HttpStatusCode.OK, (await alpha.GetAsync($"/api/v1/accounts/{own}")).StatusCode);
        using (var control = await world.SendAsync(HttpMethod.Get, "/info", world.Full.Secret))
        {
            Assert.Equal(HttpStatusCode.OK, control.StatusCode);
        }

        foreach (var route in RoutesOf(world))
        {
            using var request = new HttpRequestMessage(route.Method, BillingContractWorld.ProvisioningRoute + route.Path);
            request.Headers.Add(IdempotencyHeaders.Key, "login-" + Guid.NewGuid().ToString("N"));
            if (route.Body is not null)
            {
                request.Content = JsonContent.Create(route.Body);
            }

            using var response = await alpha.SendAsync(request);
            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized,
                $"A reseller's login on '{route.Name}': expected 401 but the panel answered {(int)response.StatusCode}.");
        }

        using var client = world.World.Factory.CreateClient();
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, $"/api/v1/accounts/{own}"),
            (HttpMethod.Post, $"/api/v1/accounts/{own}/suspend"),
            (HttpMethod.Post, $"/api/v1/accounts/{own}/reactivate"),
            (HttpMethod.Delete, $"/api/v1/accounts/{own}"),
        })
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Api-Key", world.Full.Secret);
            using var response = await client.SendAsync(request);
            Assert.True(
                response.StatusCode == HttpStatusCode.Unauthorized,
                $"The reseller's key on {method} {path}: expected 401 but the panel answered {(int)response.StatusCode}.");
        }

        await AssertNothingRanAsync(world);
    }

    /// <summary>
    /// The keys of a suspended reseller are refused on every route from the next call, and stay refused once the
    /// reseller is reactivated: the suspension revoked them (issue #551), so new keys have to be issued.
    /// </summary>
    [Fact]
    public async Task The_keys_of_a_suspended_reseller_are_refused_on_every_route_and_reactivation_does_not_revive_them()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);
        using var admin = await world.World.SignInAsync("admin");
        var resellerId = world.World.Alpha.ResellerId;

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/resellers/{resellerId}/suspend", null)).StatusCode);
        await AssertAnsweredAsync(world, RoutesOf(world), world.Full.Secret, HttpStatusCode.Unauthorized, "a suspended reseller's key");
        Assert.Empty(world.Agent.Created);
        Assert.Empty(world.Agent.Deleted);
        Assert.Empty(world.Backup.Creates);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/resellers/{resellerId}/reactivate", null)).StatusCode);
        await AssertAnsweredAsync(world, RoutesOf(world), world.Full.Secret, HttpStatusCode.Unauthorized, "a revoked key after reactivation");
    }

    /// <summary>
    /// The accepting control for all of the above: the right key succeeds on every route, in the order a
    /// provider uses them, so the refusals are not what every caller meets.
    /// </summary>
    [Fact]
    public async Task The_right_key_succeeds_on_every_route()
    {
        await using var world = await BillingContractWorld.BootAsync(_pg);
        var expected = new Dictionary<string, HttpStatusCode>
        {
            ["server info"] = HttpStatusCode.OK,
            ["read account"] = HttpStatusCode.OK,
            ["read usage"] = HttpStatusCode.OK,
            ["create"] = HttpStatusCode.Created,
            ["suspend"] = HttpStatusCode.OK,
            ["reactivate"] = HttpStatusCode.OK,
            ["change plan"] = HttpStatusCode.OK,
            ["terminate"] = HttpStatusCode.OK,
        };

        foreach (var route in RoutesOf(world))
        {
            using var response = await SendAsync(world, route, world.Full.Secret);

            Assert.True(
                response.StatusCode == expected[route.Name],
                $"'{route.Name}': expected {(int)expected[route.Name]} but the panel answered {(int)response.StatusCode}: "
                    + await response.Content.ReadAsStringAsync());
        }

        Assert.Contains("authz1", world.Agent.Created);
        Assert.Contains(world.World.Alpha.AccountName, world.Agent.Deleted);
    }
}
