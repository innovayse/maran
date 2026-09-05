using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Authorization;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
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
/// examples.
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
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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

    private static async Task SeedSteeredAdministratorAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identity.Database.MigrateAsync();

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, clock.UtcNow));

        // Forced 2FA ON, and this administrator holds no second factor: exactly the state the
        // steering exists for.
        identity.SecurityPolicies.Add(new SecurityPolicy(
            SecurityPolicy.DefaultMinimumPasswordLength,
            forceTwoFactorForAdmins: true,
            SecurityPolicy.DefaultMaxFailedLoginAttempts,
            SecurityPolicy.DefaultLockoutMinutes,
            clock.UtcNow));

        await identity.SaveChangesAsync();
    }

    private static async Task<string> SignInAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "admin", Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("session").GetProperty("requiresTwoFactorSetup").GetBoolean());

        var token = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token!;
    }

    /// <summary>Every route the host published, with the ones this steering does not govern removed.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="exempt">
    /// <c>true</c> to return the enrolment endpoints instead of the governed ones.
    /// </param>
    /// <returns>Verb-and-path pairs ready to be called.</returns>
    private static List<(string Verb, string Path)> Routes(WebApplicationFactory<Program> factory, bool exempt)
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

            var isExempt = endpoint.Metadata.GetMetadata<AllowDuringTwoFactorEnrolmentAttribute>() is not null;
            if (isExempt != exempt)
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
        await SeedSteeredAdministratorAsync(factory);
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

    /// <summary>A steered administrator can still reach the enrolment endpoints.</summary>
    /// <remarks>
    /// The other half, and the one that stops the steering from being a locked door with no key: an
    /// administrator who cannot reach enrolment can never leave the state the panel put them in.
    /// </remarks>
    [Fact]
    public async Task A_steered_administrator_can_still_reach_the_enrolment_endpoints()
    {
        await using var factory = CreateFactory();
        await SeedSteeredAdministratorAsync(factory);
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
}
