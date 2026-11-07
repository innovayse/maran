using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The whole administrator-only surface of the composed panel, DISCOVERED from the route table rather than
/// listed by hand, refuses a reseller and a customer with 403 over real HTTP against a real PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is discovered.</b> Every route endpoint the booted host serves whose authorization metadata
/// includes the administrator policy, that is not anonymous, and that does not opt into the API-key
/// scheme. Policies on a class and on an action combine (both must pass), so an action marked
/// administrator-only inside a customer-level controller is in the set. A new administrator-only endpoint
/// is therefore covered the day it exists, without anyone extending a list; the hand-written route list
/// in <see cref="ResellerAdminSurfaceAuthorizationTests"/> stays as the readable subset.
/// </para>
/// <para>
/// <b>The guards against a walk gone blind.</b> The discovered set must contain known administrator routes
/// by route key, must not contain a route a reseller legitimately reaches nor an anonymous one, and the
/// predicate is proven on hand-built endpoints that it must accept and ones it must refuse. The refusal
/// tests then have an inverse control: an endpoint a reseller may call answers it something other than 403,
/// and an administrator on the discovered routes is not refused.
/// </para>
/// <para>
/// UNOBSERVED HERE: the status of an administrator on the mutating routes (calling them would change
/// state), and any route a module maps by means other than an endpoint carrying authorize metadata.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AdministratorSurfaceDiscoveredAuthorizationTests : IAsyncLifetime
{
    /// <summary>Matches one route parameter, with or without a catch-all marker, optional marker or constraint.</summary>
    private static readonly Regex Parameter = new(@"\{\*{0,2}([^:}?]+)\??(:[^}]*)?\}", RegexOptions.Compiled);

    /// <summary>Administrator routes the discovery must find, spelled as the route key spells them.</summary>
    private static readonly string[] KnownAdministratorRoutes =
    {
        "GET /api/v1/resellers",
        "GET /api/v1/resellers/{id}",
        "POST /api/v1/resellers",
        "GET /api/v1/security-policy",
        "PUT /api/v1/security-policy",
        "GET /api/v1/notifications/smtp",
        "GET /api/v1/audit",
    };

    /// <summary>Read-only administrator routes safe to call as an administrator.</summary>
    private static readonly string[] AdministratorReads =
    {
        "/api/v1/resellers",
        "/api/v1/security-policy",
        "/api/v1/notifications/smtp",
        "/api/v1/audit",
    };

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AdministratorSurfaceDiscoveredAuthorizationTests(PostgresFixture postgres)
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
    /// The discovery finds the known administrator routes and neither the routes a reseller may call nor
    /// the anonymous ones.
    /// </summary>
    [Fact]
    public async Task Discovery_finds_the_known_administrator_routes_and_leaves_out_reseller_and_anonymous_ones()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();

        var keys = Discover(world.Factory).Select(call =>
        {
            return call.Key;
        }).ToHashSet(StringComparer.Ordinal);

        foreach (var known in KnownAdministratorRoutes)
        {
            Assert.Contains(known, keys);
        }

        Assert.DoesNotContain("GET /api/v1/plans", keys);
        Assert.DoesNotContain("POST /api/v1/auth/login", keys);
        Assert.DoesNotContain("GET /health", keys);
    }

    /// <summary>The predicate accepts an administrator-only endpoint, alone or beside a customer-level policy.</summary>
    [Fact]
    public void The_predicate_accepts_an_administrator_only_endpoint_alone_or_beside_another_policy()
    {
        var alone = Planted(new AuthorizeAttribute { Policy = AuthorizationPolicies.AdminOnly });
        var combined = Planted(
            new AuthorizeAttribute { Policy = AuthorizationPolicies.AnyAuthenticated },
            new AuthorizeAttribute { Policy = AuthorizationPolicies.AdminOnly });

        Assert.True(IsAdministratorOnly(alone));
        Assert.True(IsAdministratorOnly(combined));
    }

    /// <summary>The predicate refuses endpoints a reseller or an API key may reach, and anonymous ones.</summary>
    [Fact]
    public void The_predicate_refuses_reseller_level_anonymous_and_api_key_endpoints()
    {
        var either = Planted(new AuthorizeAttribute { Policy = AuthorizationPolicies.AdminOrReseller });
        var customer = Planted(new AuthorizeAttribute { Policy = AuthorizationPolicies.AnyAuthenticated });
        var anonymous = Planted(
            new AuthorizeAttribute { Policy = AuthorizationPolicies.AdminOnly },
            new AllowAnonymousAttribute());
        var apiKey = Planted(
            new AuthorizeAttribute { Policy = AuthorizationPolicies.AdminOnly },
            new AuthorizeAttribute { Policy = ApiKeyPolicyNames.ServerRead, AuthenticationSchemes = ApiKeyDefaults.Scheme });

        Assert.False(IsAdministratorOnly(either));
        Assert.False(IsAdministratorOnly(customer));
        Assert.False(IsAdministratorOnly(anonymous));
        Assert.False(IsAdministratorOnly(apiKey));
    }

    /// <summary>
    /// A signed-in reseller is refused 403 on every discovered administrator-only endpoint, and nothing is
    /// changed by the attempts.
    /// </summary>
    [Fact]
    public async Task A_signed_in_reseller_is_refused_every_discovered_administrator_endpoint_with_forbidden()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");

        var offenders = await RefusalsMissingAsync(alpha, Discover(world.Factory));

        Assert.True(offenders.Count == 0, "Answered something other than 403: " + string.Join("; ", offenders));
        Assert.Equal(2, await world.AccountsAsync(async accounts => { return await accounts.Resellers.IgnoreQueryFilters().CountAsync(); }));
    }

    /// <summary>A signed-in customer is refused 403 on every discovered administrator-only endpoint.</summary>
    [Fact]
    public async Task A_signed_in_customer_is_refused_every_discovered_administrator_endpoint_with_forbidden()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        await EndpointHarness.SeedCustomerAsync(world.Factory, "olive", world.Alpha.AccountId);
        using var olive = await world.SignInAsync("olive");

        var offenders = await RefusalsMissingAsync(olive, Discover(world.Factory));

        Assert.True(offenders.Count == 0, "Answered something other than 403: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// The inverse controls: an endpoint a reseller may call does not answer it 403, and an administrator is
    /// not refused on the discovered read-only routes, so the refusals above are the role and not a host that
    /// refuses everyone.
    /// </summary>
    [Fact]
    public async Task A_reseller_endpoint_is_open_to_a_reseller_and_the_administrator_reads_are_open_to_an_administrator()
    {
        await using var world = await new ResellerFixtureBuilder(_pg).BuildAsync();
        using var alpha = await world.SignInAsync("alpha");
        using var admin = await world.SignInAsync("admin");
        var keys = Discover(world.Factory).Select(call =>
        {
            return call.Key;
        }).ToHashSet(StringComparer.Ordinal);

        var plans = await alpha.GetAsync("/api/v1/plans");

        Assert.NotEqual(HttpStatusCode.Forbidden, plans.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        foreach (var route in AdministratorReads)
        {
            Assert.Contains("GET " + route, keys);
            var response = await admin.GetAsync(route);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    /// <summary>Sends every discovered call as <paramref name="client"/> and names each that was not refused 403.</summary>
    /// <param name="client">The signed-in caller's client.</param>
    /// <param name="calls">The discovered calls.</param>
    /// <returns>One description per call that answered anything but 403; empty when all were refused.</returns>
    private static async Task<List<string>> RefusalsMissingAsync(
        HttpClient client,
        List<(string Key, string Method, string Path)> calls)
    {
        var offenders = new List<string>();
        foreach (var (key, method, path) in calls)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method is "POST" or "PUT" or "PATCH")
            {
                request.Content = JsonContent.Create(new { });
            }

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                offenders.Add($"{key} -> {(int)response.StatusCode}");
            }
        }

        return offenders;
    }

    /// <summary>Reads the booted host's route table and returns one call per verb of every administrator-only endpoint.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>The route key, the verb and a concrete path with every parameter filled in, in table order.</returns>
    private static List<(string Key, string Method, string Path)> Discover(WebApplicationFactory<Program> factory)
    {
        var calls = new List<(string Key, string Method, string Path)>();
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        foreach (var endpoint in endpoints.Where(IsAdministratorOnly))
        {
            var template = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            var path = Parameter.Replace(template, ConcreteValue);
            var verbs = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            foreach (var verb in verbs is { Count: > 0 } ? verbs : new[] { "GET" })
            {
                var key = verb + " " + Parameter.Replace(template, "{$1}");
                calls.Add((key, verb, path));
            }
        }

        return calls;
    }

    /// <summary>Whether an endpoint is non-anonymous, API-key-free and carries the administrator policy.</summary>
    /// <param name="endpoint">The endpoint to judge.</param>
    /// <returns><c>true</c> when only an administrator may pass its authorization.</returns>
    private static bool IsAdministratorOnly(RouteEndpoint endpoint)
    {
        if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return false;
        }

        var data = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
        if (data.Any(item => { return !string.IsNullOrEmpty(item.AuthenticationSchemes); }))
        {
            return false;
        }

        return data.Any(item => { return string.Equals(item.Policy, AuthorizationPolicies.AdminOnly, StringComparison.Ordinal); });
    }

    /// <summary>Gives a route parameter a value its constraint accepts, so routing reaches authorization.</summary>
    /// <param name="match">The parameter as written in the template.</param>
    /// <returns>A fresh GUID for a GUID-constrained parameter, otherwise a plain segment.</returns>
    private static string ConcreteValue(Match match)
    {
        return match.Groups[2].Value.Contains("guid", StringComparison.OrdinalIgnoreCase)
            ? Guid.NewGuid().ToString()
            : "x";
    }

    /// <summary>Builds a <c>GET /planted/x</c> endpoint carrying exactly the given metadata.</summary>
    /// <param name="metadata">The metadata objects the endpoint carries besides its verb.</param>
    /// <returns>The planted endpoint.</returns>
    private static RouteEndpoint Planted(params object[] metadata)
    {
        var all = new List<object> { new HttpMethodMetadata(["GET"]) };
        all.AddRange(metadata);

        return new RouteEndpoint(
            _ =>
            {
                return Task.CompletedTask;
            },
            RoutePatternFactory.Parse("/planted/x"),
            order: 0,
            new EndpointMetadataCollection(all),
            displayName: "planted");
    }
}
