using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Authorization;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The forced two-factor steering, checked by WALKING THE ROUTE TABLE rather than by naming three
/// examples, for an administrator and for a reseller (issue #179, decision D4): a reseller can start
/// view-as into its customers, so its login is steered exactly as an administrator's is.
/// </summary>
/// <remarks>
/// <para>
/// The property is "an administrator who must still enrol can reach the enrolment endpoints and
/// NOTHING else". Three examples cannot say that: the interesting case is always the endpoint
/// somebody adds next, and a hand-written list of routes is a list that goes quietly out of date in
/// the permissive direction. So this test asks the running host for every endpoint it actually
/// published, filters out the ones that are anonymous or explicitly marked as part of enrolment, and
/// asserts that every single one of the rest refuses.
/// </para>
/// <para>
/// <b>The refusal is 403, and it is the one deliberate exception to this plan's 404-not-403 rule.</b>
/// Everywhere else a caller who may not have a thing is told it does not exist, because a 403
/// confirms existence to somebody probing. Here the caller is authenticated, the panel knows exactly
/// who they are, and they are being steered rather than probed — a 404 would tell a legitimate
/// administrator that the panel they just signed into has no screens at all.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class TwoFactorEnrolmentSteeringTests : IAsyncLifetime
{
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string Password = "correct horse battery staple";

    /// <summary>The reseller login the reseller facts steer; its name is also the reseller's.</summary>
    private const string ResellerLogin = "nova";

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public TwoFactorEnrolmentSteeringTests(PostgresFixture postgres)
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

    private WebApplicationFactory<Program> CreateFactory()
    {
        return new ConfiguredHostFactory(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);
            builder.UseSetting("RateLimiting:ApiPermitLimit", "100000");

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }

    /// <summary>
    /// Stores a login that must still enrol, under a policy forcing a second factor for administrators
    /// and for resellers alike: exactly the state the steering exists for.
    /// </summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="role">The role of the login: <c>Admin</c> (named admin) or <c>Reseller</c> (named nova).</param>
    private static async Task SeedSteeredLoginAsync(WebApplicationFactory<Program> factory, UserRole role)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identity.Database.MigrateAsync();

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        if (role == UserRole.Reseller)
        {
            // The steering refuses before any handler reads the reseller, so the login needs no
            // accounts-module row behind it.
            var reseller = User.InviteReseller(
                Guid.NewGuid(), ResellerLogin, $"{ResellerLogin}@example.com", Guid.NewGuid(), clock.UtcNow);
            reseller.Activate(hasher.Hash(Password));
            identity.Users.Add(reseller);
        }
        else
        {
            identity.Users.Add(new User(
                Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, clock.UtcNow));
        }

        // Forced 2FA ON for both roles, and this login holds no second factor.
        identity.SecurityPolicies.Add(new SecurityPolicy(
            SecurityPolicy.DefaultMinimumPasswordLength,
            forceTwoFactorForAdmins: true,
            forceTwoFactorForResellers: true,
            SecurityPolicy.DefaultMaxFailedLoginAttempts,
            SecurityPolicy.DefaultLockoutMinutes,
            clock.UtcNow));

        await identity.SaveChangesAsync();
    }

    private static async Task<string> SignInAsync(HttpClient client, string username = "admin")
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = username, Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("session").GetProperty("requiresTwoFactorSetup").GetBoolean());

        var token = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token!;
    }

    /// <summary>
    /// Whether an endpoint is one only an API key reaches: it names a policy that is a member of
    /// <see cref="ApiKeyPolicyNames"/> AND the <c>ApiKey</c> authentication scheme.
    /// </summary>
    /// <param name="endpoint">The endpoint to classify.</param>
    /// <returns><c>true</c> when the endpoint authenticates with the API key scheme alone.</returns>
    /// <remarks>
    /// The steering is a rule about a signed-in LOGIN, and a JWT principal never reaches these
    /// endpoints: the scheme they name is the key handler's, which reads <c>X-Api-Key</c> and finds
    /// none, so the answer is 401 whoever holds the bearer token. That 401 discloses nothing, and a
    /// steered login cannot use a key endpoint to get around enrolment. They are therefore not
    /// governed by the steering and are checked separately by
    /// <see cref="A_steered_administrator_gets_no_identity_on_the_api_key_endpoints"/>.
    /// </remarks>
    private static bool IsApiKeyOnly(RouteEndpoint endpoint)
    {
        var apiKeyPolicies = typeof(ApiKeyPolicyNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => { return field.IsLiteral; })
            .Select(field => { return (string?)field.GetRawConstantValue(); })
            .ToHashSet();

        return endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(item =>
        {
            return item.Policy is not null
                && apiKeyPolicies.Contains(item.Policy)
                && string.Equals(item.AuthenticationSchemes, ApiKeyDefaults.Scheme, StringComparison.Ordinal);
        });
    }

    /// <summary>Every route the host published, with the ones this steering does not govern removed.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="exempt">
    /// <c>true</c> to return the enrolment endpoints instead of the governed ones.
    /// </param>
    /// <param name="apiKeyOnly">
    /// <c>true</c> to return the endpoints only an API key reaches instead; they are in neither of the
    /// other two lists.
    /// </param>
    /// <returns>Verb-and-path pairs ready to be called.</returns>
    private static List<(string Verb, string Path)> Routes(
        WebApplicationFactory<Program> factory,
        bool exempt,
        bool apiKeyOnly = false)
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var walked = new List<(string, string)>();

        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            // Anonymous endpoints never reach authorization at all, so the steering has nothing to
            // say about them — and they are the sign-in, refresh and reset surfaces a steered
            // administrator legitimately needs.
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                continue;
            }

            if (IsApiKeyOnly(endpoint) != apiKeyOnly)
            {
                continue;
            }

            var isExempt = endpoint.Metadata.GetMetadata<AllowDuringTwoFactorEnrolmentAttribute>() is not null;
            if (!apiKeyOnly && isExempt != exempt)
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            var verb = methods is { Count: > 0 } ? methods[0] : "GET";
            walked.Add((verb, "/" + Fill(endpoint.RoutePattern.RawText ?? string.Empty)));
        }

        return walked;
    }

    /// <summary>Replaces every route parameter with a value that binds, so the request reaches routing.</summary>
    /// <param name="template">The raw route template.</param>
    /// <returns>A concrete path.</returns>
    /// <remarks>
    /// A GUID satisfies both the constrained parameters and the free ones. Nothing here has to
    /// EXIST: authorization runs before the handler, so a governed endpoint refuses before it ever
    /// looks the resource up — which is precisely the ordering this test relies on.
    /// </remarks>
    private static string Fill(string template)
    {
        return Regex.Replace(
            template,
            @"\{[^}]+\}",
            Guid.NewGuid().ToString(),
            RegexOptions.None,
            TimeSpan.FromSeconds(1));
    }

    private static async Task<HttpResponseMessage> CallAsync(HttpClient client, string verb, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(verb), path);
        if (verb is "POST" or "PUT" or "PATCH")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    /// <summary>A steered administrator is refused by every endpoint that is not part of enrolment.</summary>
    [Fact]
    public async Task A_steered_administrator_is_refused_by_every_endpoint_that_is_not_part_of_enrolment()
    {
        await using var factory = CreateFactory();
        await SeedSteeredLoginAsync(factory, UserRole.Admin);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await SignInAsync(client));

        var governed = Routes(factory, exempt: false);

        // The walk must find something. A filter that accidentally matched nothing would make this
        // test pass while asserting nothing at all — the failure mode rules/testing.md calls out.
        Assert.NotEmpty(governed);

        var reachable = new List<string>();
        foreach (var (verb, path) in governed)
        {
            using var response = await CallAsync(client, verb, path);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                reachable.Add($"{verb} {path} -> {(int)response.StatusCode}");
            }
        }

        Assert.Empty(reachable);
    }

    /// <summary>A steered administrator gets no identity on the API key endpoints.</summary>
    /// <remarks>
    /// The API key endpoints are outside the steering walk (<see cref="IsApiKeyOnly"/>), so this is
    /// their own check and the walk's accepting control: every one of them answers a steered login's
    /// bearer token 401, the key handler finding no key, and none answers 200.
    /// </remarks>
    [Fact]
    public async Task A_steered_administrator_gets_no_identity_on_the_api_key_endpoints()
    {
        await using var factory = CreateFactory();
        await SeedSteeredLoginAsync(factory, UserRole.Admin);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await SignInAsync(client));

        var keyRoutes = Routes(factory, exempt: false, apiKeyOnly: true);

        // The two endpoints that opt in today. An exemption that matched nothing would let a route
        // slip out of the walk unchecked, so the set must contain what it is known to contain.
        Assert.Contains(keyRoutes, route => { return route.Path == "/api/v1/api-keys/self"; });
        Assert.Contains(keyRoutes, route => { return route.Path == "/api/v1/server/status"; });

        foreach (var (verb, path) in keyRoutes)
        {
            using var response = await CallAsync(client, verb, path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    /// <summary>A steered administrator can still reach the enrolment endpoints.</summary>
    /// <remarks>
    /// The other half, and the one that stops the steering from being a locked door with no key: an
    /// administrator who cannot reach enrolment can never leave the state the panel put them in.
    /// </remarks>
    [Fact]
    public async Task A_steered_administrator_can_still_reach_the_enrolment_endpoints()
    {
        await using var factory = CreateFactory();
        await SeedSteeredLoginAsync(factory, UserRole.Admin);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await SignInAsync(client));

        var exempt = Routes(factory, exempt: true);
        Assert.NotEmpty(exempt);

        foreach (var (verb, path) in exempt)
        {
            using var response = await CallAsync(client, verb, path);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    /// <summary>An administrator with two factor off is not steered when the policy does not force it.</summary>
    [Fact]
    public async Task An_administrator_with_two_factor_off_is_not_steered_when_the_policy_does_not_force_it()
    {
        await using var factory = CreateFactory();

        using (var scope = factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await identity.Database.MigrateAsync();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            identity.Users.Add(new User(
                Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, clock.UtcNow));
            await identity.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "admin", Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("session").GetProperty("requiresTwoFactorSetup").GetBoolean());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.RootElement.GetProperty("session").GetProperty("accessToken").GetString());

        using var sessions = await CallAsync(client, "GET", "/api/v1/sessions");
        Assert.Equal(HttpStatusCode.OK, sessions.StatusCode);
    }

    /// <summary>A steered reseller is refused by every endpoint that is not part of enrolment.</summary>
    [Fact]
    public async Task A_steered_reseller_is_refused_by_every_endpoint_that_is_not_part_of_enrolment()
    {
        await using var factory = CreateFactory();
        await SeedSteeredLoginAsync(factory, UserRole.Reseller);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await SignInAsync(client, ResellerLogin));

        var governed = Routes(factory, exempt: false);

        // The walk must find something, and it must reach the surfaces a reseller is allowed on: a
        // filter that matched nothing would pass while asserting nothing.
        Assert.NotEmpty(governed);
        Assert.Contains(governed, route =>
        {
            return route.Verb == "GET" && route.Path.StartsWith("/api/v1/reseller/me", StringComparison.Ordinal);
        });
        Assert.Contains(governed, route =>
        {
            return route.Verb == "POST" && route.Path == "/api/v1/view-as";
        });

        var reachable = new List<string>();
        foreach (var (verb, path) in governed)
        {
            using var response = await CallAsync(client, verb, path);
            if (response.StatusCode != HttpStatusCode.Forbidden)
            {
                reachable.Add($"{verb} {path} -> {(int)response.StatusCode}");
            }
        }

        Assert.Empty(reachable);
    }

    /// <summary>A steered reseller can still reach the enrolment endpoints.</summary>
    /// <remarks>
    /// The other half, which stops the steering from being a locked door with no key: a reseller that
    /// cannot reach enrolment can never leave the state the panel put it in.
    /// </remarks>
    [Fact]
    public async Task A_steered_reseller_can_still_reach_the_enrolment_endpoints()
    {
        await using var factory = CreateFactory();
        await SeedSteeredLoginAsync(factory, UserRole.Reseller);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await SignInAsync(client, ResellerLogin));

        var exempt = Routes(factory, exempt: true);
        Assert.NotEmpty(exempt);

        foreach (var (verb, path) in exempt)
        {
            using var response = await CallAsync(client, verb, path);
            Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    /// <summary>A reseller with two factor off is not steered when the policy does not force it.</summary>
    /// <remarks>
    /// The policy's reseller switch is honoured, in both directions: forced it steers (above), saved off
    /// it does not, and the administrator's own switch is a separate one.
    /// </remarks>
    [Fact]
    public async Task A_reseller_with_two_factor_off_is_not_steered_when_the_policy_does_not_force_it()
    {
        await using var factory = CreateFactory();

        using (var scope = factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            await identity.Database.MigrateAsync();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var reseller = User.InviteReseller(
                Guid.NewGuid(), ResellerLogin, $"{ResellerLogin}@example.com", Guid.NewGuid(), clock.UtcNow);
            reseller.Activate(hasher.Hash(Password));
            identity.Users.Add(reseller);

            // Administrators are forced and resellers are not: the two switches are independent.
            identity.SecurityPolicies.Add(new SecurityPolicy(
                SecurityPolicy.DefaultMinimumPasswordLength,
                forceTwoFactorForAdmins: true,
                forceTwoFactorForResellers: false,
                SecurityPolicy.DefaultMaxFailedLoginAttempts,
                SecurityPolicy.DefaultLockoutMinutes,
                clock.UtcNow));
            await identity.SaveChangesAsync();
        }

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = ResellerLogin, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("session").GetProperty("requiresTwoFactorSetup").GetBoolean());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.RootElement.GetProperty("session").GetProperty("accessToken").GetString());

        using var sessions = await CallAsync(client, "GET", "/api/v1/sessions");
        Assert.Equal(HttpStatusCode.OK, sessions.StatusCode);
    }
}
