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
/// The first-password gate (issue #194), checked on the running host: a customer login created with an
/// initial password signs in, is refused by EVERY endpoint but the password change, changes its password,
/// and is then free.
/// </summary>
/// <remarks>
/// <para>
/// <b>The refusal is read from the body, not only the status.</b> A role refusal and the gate are both
/// 403, so a walk that only counted 403s would pass for an endpoint the role policy refused for another
/// reason. The gate's own code, <c>HostPasswordChangeRequired</c>, outranks every other failed
/// requirement in the problem writer, so it is the code every governed endpoint must answer.
/// </para>
/// <para>
/// Like the two-factor walk it reads the route table the host published, so the endpoint somebody adds
/// next is covered without anyone extending a list. Endpoints that are anonymous or reachable only with an
/// API key are outside it: the first never reach authorization, and the second never see a login.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class PasswordChangeGateEndpointTests : IAsyncLifetime
{
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string InitialPassword = "first horse battery staple";
    private const string ChosenPassword = "second horse battery staple";
    private const string Login = "owner-one";

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public PasswordChangeGateEndpointTests(PostgresFixture postgres)
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

    /// <summary>Stores the login exactly as the account-creation handler does for an initial password.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedOwnerWithInitialPasswordAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await identity.Database.MigrateAsync();

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var owner = User.CreateWithPassword(
            Guid.NewGuid(), Login, $"{Login}@example.com", hasher.Hash(InitialPassword), clock.UtcNow);
        identity.Users.Add(owner);
        identity.AccountMemberships.Add(AccountMembership.Own(Guid.NewGuid(), owner.Id, Guid.NewGuid(), clock.UtcNow));
        await identity.SaveChangesAsync();
    }

    /// <summary>Signs in and returns the body's session element.</summary>
    /// <param name="client">The client.</param>
    /// <param name="password">The password to sign in with.</param>
    /// <returns>The parsed response body; the caller disposes it.</returns>
    private static async Task<JsonDocument> SignInAsync(HttpClient client, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = Login, Password = password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Whether an endpoint is one only an API key reaches.</summary>
    /// <param name="endpoint">The endpoint to classify.</param>
    /// <returns>True when it names an API key policy together with the key authentication scheme.</returns>
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

    /// <summary>The routes the gate governs, or the ones it exempts.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="exempt">True to return the endpoints marked as the password change.</param>
    /// <returns>Verb-and-path pairs ready to be called.</returns>
    private static List<(string Verb, string Path)> Routes(WebApplicationFactory<Program> factory, bool exempt)
    {
        var walked = new List<(string, string)>();

        foreach (var endpoint in factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            // The endpoints that answer to another credential (an API key, a single-use export grant) never
            // see a session, so the gate has nothing to say about them.
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                || IsApiKeyOnly(endpoint)
                || MigrationGrantEndpoint.IsGrantOnly(endpoint))
            {
                continue;
            }

            var isExempt = endpoint.Metadata.GetMetadata<AllowDuringPasswordChangeAttribute>() is not null;
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

    /// <summary>Replaces every route parameter with a GUID, which binds to both constrained and free parameters.</summary>
    /// <param name="template">The raw route template.</param>
    /// <returns>A concrete path.</returns>
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

    /// <summary>Sign-in with the initial password succeeds and says the password must be changed.</summary>
    [Fact]
    public async Task Signing_in_with_the_initial_password_succeeds_and_says_the_password_must_be_changed()
    {
        await using var factory = CreateFactory();
        await SeedOwnerWithInitialPasswordAsync(factory);
        using var client = factory.CreateClient();

        using var body = await SignInAsync(client, InitialPassword);

        var session = body.RootElement.GetProperty("session");
        Assert.True(session.GetProperty("requiresPasswordChange").GetBoolean());
        Assert.False(session.GetProperty("requiresTwoFactorSetup").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("accessToken").GetString()));
    }

    /// <summary>Every governed endpoint refuses the restricted token with the gate's own code.</summary>
    [Fact]
    public async Task Every_endpoint_but_the_password_change_refuses_the_restricted_token_with_the_gates_code()
    {
        await using var factory = CreateFactory();
        await SeedOwnerWithInitialPasswordAsync(factory);
        using var client = factory.CreateClient();
        using (var body = await SignInAsync(client, InitialPassword))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", body.RootElement.GetProperty("session").GetProperty("accessToken").GetString());
        }

        var governed = Routes(factory, exempt: false);

        // The walk must find something, and the exemption must be what it is known to be: a filter that
        // matched nothing would pass while asserting nothing.
        Assert.NotEmpty(governed);
        var exempt = Routes(factory, exempt: true);
        Assert.Contains(exempt, route => { return route.Path == "/api/v1/auth/change-password"; });

        var reachable = new List<string>();
        foreach (var (verb, path) in governed)
        {
            using var response = await CallAsync(client, verb, path);
            var text = await response.Content.ReadAsStringAsync();
            if (response.StatusCode != HttpStatusCode.Forbidden
                || !text.Contains("HostPasswordChangeRequired", StringComparison.Ordinal))
            {
                reachable.Add($"{verb} {path} -> {(int)response.StatusCode}");
            }
        }

        Assert.Empty(reachable);
    }

    /// <summary>Changing the password frees the login: the next sign-in is unrestricted and reaches ordinary endpoints.</summary>
    /// <remarks>The accepting half: a gate that never opened would pass every refusal above.</remarks>
    [Fact]
    public async Task Changing_the_password_frees_the_login()
    {
        await using var factory = CreateFactory();
        await SeedOwnerWithInitialPasswordAsync(factory);
        using var client = factory.CreateClient();
        using (var body = await SignInAsync(client, InitialPassword))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", body.RootElement.GetProperty("session").GetProperty("accessToken").GetString());
        }

        using var blocked = await CallAsync(client, "GET", "/api/v1/sessions");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        var change = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new { CurrentPassword = InitialPassword, NewPassword = ChosenPassword });
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        using var again = await SignInAsync(client, ChosenPassword);
        var session = again.RootElement.GetProperty("session");
        Assert.False(session.GetProperty("requiresPasswordChange").GetBoolean());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", session.GetProperty("accessToken").GetString());
        using var free = await CallAsync(client, "GET", "/api/v1/sessions");
        Assert.Equal(HttpStatusCode.OK, free.StatusCode);
    }
}
