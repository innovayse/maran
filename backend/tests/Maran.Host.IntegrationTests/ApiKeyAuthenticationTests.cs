using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Maran.Host.Configuration;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The <c>ApiKey</c> authentication scheme over real HTTP, driven through <c>GET api/v1/api-keys/self</c>,
/// the one endpoint every valid key may call (issue #144): what is accepted, that every refusal is the
/// same answer, that a key reaches nothing else, and what a call leaves behind.
/// </summary>
/// <remarks>
/// Threat note: docs/superpowers/notes/2026-11-05-api-keys-threat-note.md (second review OUTSTANDING).
/// Each test names the client address its requests come from through <c>X-Forwarded-For</c> behind a
/// loopback peer, which the panel trusts as its proxy; the failed-attempt limiter counts per address, so
/// tests that are not about lockout use distinct addresses and never share a budget.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ApiKeyAuthenticationTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ApiKeyAuthenticationTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    /// <returns>Resolves once this test's database exists.</returns>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    /// <returns>Resolves immediately; the shared server outlives the test.</returns>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A valid key is accepted and the endpoint describes that key without its secret.</summary>
    [Fact]
    public async Task A_valid_key_is_accepted_and_described_without_its_secret()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var issued = await host.CreateKeyAsync(admin, "ci deploy");

        var response = await host.GetAsync(ApiKeyHost.SelfRoute, issued.Secret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.Equal(issued.Id, body.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("ci deploy", body.RootElement.GetProperty("name").GetString());
        Assert.Contains(ApiKeyScopes.SelfRead, body.RootElement.GetProperty("scopes").EnumerateArray().Select(scope => scope.GetString()));
        Assert.DoesNotContain(issued.Secret, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every way of being refused answers with the same status, the same body and the same headers, so the
    /// answer discloses nothing about whether a prefix exists or why a key was refused (the no-oracle requirement).
    /// </summary>
    [Fact]
    public async Task Every_refusal_answers_with_byte_identical_status_body_and_headers()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var valid = await host.CreateKeyAsync(admin, "valid");
        var bound = await host.CreateKeyAsync(admin, "bound", allowedCidrs: ["203.0.113.0/24"]);
        var revoked = await host.CreateKeyAsync(admin, "revoked");
        await host.RevokeKeyAsync(admin, revoked.Id);
        var expiring = await host.CreateKeyAsync(admin, "expiring", expiresAt: host.Clock.UtcNow.AddHours(1));
        host.Clock.Advance(TimeSpan.FromHours(2));

        var refusals = new Dictionary<string, HttpResponseMessage>
        {
            ["no header"] = await host.GetAsync(ApiKeyHost.SelfRoute, null, "198.51.100.1"),
            ["malformed"] = await host.GetAsync(ApiKeyHost.SelfRoute, "not-a-key", "198.51.100.2"),
            ["unknown key"] = await host.GetAsync(ApiKeyHost.SelfRoute, ApiKeyHasher.Mint().Plaintext, "198.51.100.3"),
            ["wrong secret, real prefix"] = await host.GetAsync(ApiKeyHost.SelfRoute, $"mk_{valid.Prefix}_{new string('A', 43)}", "198.51.100.4"),
            ["revoked"] = await host.GetAsync(ApiKeyHost.SelfRoute, revoked.Secret, "198.51.100.5"),
            ["expired"] = await host.GetAsync(ApiKeyHost.SelfRoute, expiring.Secret, "198.51.100.6"),
            ["outside the bound range"] = await host.GetAsync(ApiKeyHost.SelfRoute, bound.Secret, "198.51.100.7"),
        };

        // The inverse control: the harness really does let an acceptable key through.
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync(ApiKeyHost.SelfRoute, valid.Secret, "198.51.100.8")).StatusCode);

        var signatures = new Dictionary<string, string>();
        foreach (var (label, response) in refusals)
        {
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{label} answered {(int)response.StatusCode}");
            signatures[label] = await SignatureAsync(response);
        }

        var reference = signatures["no header"];
        Assert.Contains("WWW-Authenticate", reference, StringComparison.OrdinalIgnoreCase);
        foreach (var (label, signature) in signatures)
        {
            Assert.True(reference == signature, $"{label} differs from the reference refusal:\n{signature}\n--- reference ---\n{reference}");
        }
    }

    /// <summary>A key travelling anywhere but its own header is not a credential.</summary>
    /// <param name="where">Which place the key is sent in.</param>
    [Theory]
    [InlineData("query")]
    [InlineData("cookie")]
    [InlineData("bearer")]
    public async Task A_key_outside_its_header_is_refused(string where)
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var issued = await host.CreateKeyAsync(admin, "ci deploy");
        var path = where == "query" ? $"{ApiKeyHost.SelfRoute}?X-Api-Key={issued.Secret}&apiKey={issued.Secret}" : ApiKeyHost.SelfRoute;
        using var client = host.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (where == "cookie")
        {
            request.Headers.Add("Cookie", $"X-Api-Key={issued.Secret}");
        }

        if (where == "bearer")
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", issued.Secret);
        }

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A key bound to an address range is accepted from inside it and refused from outside it.</summary>
    [Fact]
    public async Task A_key_bound_to_a_range_works_only_from_inside_it()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var bound = await host.CreateKeyAsync(admin, "office", allowedCidrs: ["203.0.113.0/24"]);

        var inside = await host.GetAsync(ApiKeyHost.SelfRoute, bound.Secret, "203.0.113.9");
        var outside = await host.GetAsync(ApiKeyHost.SelfRoute, bound.Secret, "198.51.100.9");

        Assert.Equal(HttpStatusCode.OK, inside.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, outside.StatusCode);
    }

    /// <summary>A key that has passed its expiry is refused while it was accepted before it.</summary>
    [Fact]
    public async Task A_key_is_refused_once_its_expiry_has_passed()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var issued = await host.CreateKeyAsync(admin, "short lived", expiresAt: host.Clock.UtcNow.AddHours(1));

        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync(ApiKeyHost.SelfRoute, issued.Secret)).StatusCode);
        host.Clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetAsync(ApiKeyHost.SelfRoute, issued.Secret)).StatusCode);
    }

    /// <summary>Revoking a key stops it at the very next call, with no cache to wait out.</summary>
    [Fact]
    public async Task Revoking_a_key_takes_effect_on_the_very_next_call()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var issued = await host.CreateKeyAsync(admin, "ci deploy");
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync(ApiKeyHost.SelfRoute, issued.Secret)).StatusCode);

        await host.RevokeKeyAsync(admin, issued.Id);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetAsync(ApiKeyHost.SelfRoute, issued.Secret)).StatusCode);
    }

    /// <summary>
    /// A valid key reaches only endpoints that opted in: the SPA's own surface and the key management
    /// itself answer it 401 (or 403), so a stolen key can neither read accounts nor mint a successor.
    /// </summary>
    /// <param name="path">A route that has not opted in to the key scheme.</param>
    [Theory]
    [InlineData("/api/v1/accounts")]
    [InlineData("/api/v1/api-keys")]
    [InlineData("/api/v1/api-keys/scopes")]
    [InlineData("/api/v1/sessions")]
    public async Task A_valid_key_does_not_reach_an_endpoint_that_has_not_opted_in(string path)
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var issued = await host.CreateKeyAsync(admin, "everything", [ApiKeyScopes.SelfRead, ApiKeyScopes.ServerRead]);
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync(ApiKeyHost.SelfRoute, issued.Secret)).StatusCode);

        var response = await host.GetAsync(path, issued.Secret);

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"GET {path} with a key answered {(int)response.StatusCode}");
    }

    /// <summary>A panel login token is not a key: the self endpoint refuses an administrator's JWT.</summary>
    [Fact]
    public async Task A_login_token_is_refused_by_the_key_endpoint()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();

        var response = await admin.GetAsync(ApiKeyHost.SelfRoute);

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"answered {(int)response.StatusCode}");
    }

    /// <summary>The last use is recorded by a call and is not rewritten again within a minute.</summary>
    [Fact]
    public async Task The_last_use_is_recorded_and_not_rewritten_within_a_minute()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var issued = await host.CreateKeyAsync(admin, "ci deploy");
        Assert.Null((await host.ReadRowAsync(issued.Id)).LastUsedAt);

        await host.GetAsync(ApiKeyHost.SelfRoute, issued.Secret, "198.51.100.30");
        var first = await host.ReadRowAsync(issued.Id);
        host.Clock.Advance(TimeSpan.FromSeconds(30));
        await host.GetAsync(ApiKeyHost.SelfRoute, issued.Secret, "198.51.100.31");
        var within = await host.ReadRowAsync(issued.Id);
        host.Clock.Advance(TimeSpan.FromSeconds(31));
        await host.GetAsync(ApiKeyHost.SelfRoute, issued.Secret, "198.51.100.32");
        var after = await host.ReadRowAsync(issued.Id);

        Assert.NotNull(first.LastUsedAt);
        Assert.Equal("198.51.100.30", first.LastUsedIp);
        Assert.Equal(first.LastUsedAt, within.LastUsedAt);
        Assert.Equal("198.51.100.30", within.LastUsedIp);
        Assert.True(after.LastUsedAt > first.LastUsedAt, "a call after the window must move the last use");
        Assert.Equal("198.51.100.32", after.LastUsedIp);
    }

    /// <summary>
    /// After as many failed attempts as the sign-in limit allows, an address is refused even with a valid
    /// key, with the same answer as any refusal, while another address is unaffected.
    /// </summary>
    [Fact]
    public async Task Repeated_failures_lock_one_address_out_and_leave_another_alone()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var issued = await host.CreateKeyAsync(admin, "ci deploy");
        var limit = host.Factory.Services.GetRequiredService<IOptions<RateLimitOptions>>().Value.LoginMaxAttempts;
        const string locked = "198.51.100.40";
        const string other = "198.51.100.41";
        Assert.Equal(HttpStatusCode.OK, (await host.GetAsync(ApiKeyHost.SelfRoute, issued.Secret, locked)).StatusCode);

        HttpResponseMessage lastFailure = null!;
        for (var attempt = 0; attempt < limit; attempt++)
        {
            lastFailure = await host.GetAsync(ApiKeyHost.SelfRoute, ApiKeyHasher.Mint().Plaintext, locked);
            Assert.Equal(HttpStatusCode.Unauthorized, lastFailure.StatusCode);
        }

        var afterLockout = await host.GetAsync(ApiKeyHost.SelfRoute, issued.Secret, locked);
        var elsewhere = await host.GetAsync(ApiKeyHost.SelfRoute, issued.Secret, other);

        Assert.Equal(HttpStatusCode.Unauthorized, afterLockout.StatusCode);
        Assert.Equal(await SignatureAsync(lastFailure), await SignatureAsync(afterLockout));
        Assert.Equal(HttpStatusCode.OK, elsewhere.StatusCode);
    }

    /// <summary>
    /// A successful key call is journalled with the method and the route template and with neither the key
    /// nor the query string; a refused call is not journalled as a key call.
    /// </summary>
    [Fact]
    public async Task A_successful_key_call_is_journalled_with_the_route_template_and_no_secret()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var issued = await host.CreateKeyAsync(admin, "ci deploy");

        await host.GetAsync($"{ApiKeyHost.SelfRoute}?probe=query-marker", issued.Secret, "198.51.100.50");
        await host.GetAsync(ApiKeyHost.SelfRoute, ApiKeyHasher.Mint().Plaintext, "198.51.100.51");

        var entry = Assert.Single(await host.ReadAuditAsync(IdentityAuditActions.ApiKeyCall));
        Assert.Equal("GET api/v1/api-keys/self 200", entry.Subject);
        Assert.Equal("api-key:" + issued.Id.ToString("N"), entry.ActorUsername);
        Assert.Equal("198.51.100.50", entry.IpAddress);
        Assert.True(entry.Succeeded);
        var columns = string.Join('\n', entry.ActorUsername, entry.Action, entry.Subject, entry.IpAddress, entry.UserAgent, entry.CorrelationId);
        Assert.DoesNotContain(issued.Secret, columns, StringComparison.Ordinal);
        Assert.DoesNotContain("query-marker", columns, StringComparison.Ordinal);
        Assert.DoesNotContain("?", columns, StringComparison.Ordinal);
    }

    /// <summary>Flattens what a caller can observe of a response into one comparable text.</summary>
    /// <param name="response">The response.</param>
    /// <returns>Status, headers (minus the two that vary per request) and body.</returns>
    /// <remarks>
    /// The <c>Date</c> header and the correlation id are the per-request values every response carries; a
    /// reason-revealing header would survive this filter and fail the comparison, which is the point.
    /// </remarks>
    private static async Task<string> SignatureAsync(HttpResponseMessage response)
    {
        var headers = response.Headers
            .Concat(response.Content.Headers)
            .Where(header =>
            {
                return !header.Key.Equals("Date", StringComparison.OrdinalIgnoreCase)
                    && !header.Key.Contains("Correlation", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .Select(header => $"{header.Key}: {string.Join(',', header.Value)}");

        return $"{(int)response.StatusCode}\n{string.Join('\n', headers)}\n\n{await response.Content.ReadAsStringAsync()}";
    }
}
