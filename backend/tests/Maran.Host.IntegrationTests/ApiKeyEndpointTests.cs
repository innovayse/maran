using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Sdk.Contracts;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The administrator's API key management surface over real HTTP and a real journal (issue #144): create,
/// list, revoke, the scope vocabulary, who is refused, and what the journal records about each.
/// </summary>
/// <remarks>
/// Threat note: docs/superpowers/notes/2026-11-05-api-keys-threat-note.md (second review OUTSTANDING).
/// What the key does once it is issued is in <c>ApiKeyAuthenticationTests</c>.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ApiKeyEndpointTests : IAsyncLifetime
{
    /// <summary>The shape of a key: marker, eight lower-case hex characters, 43 base64url characters.</summary>
    private static readonly Regex KeyShape = new("^mk_[0-9a-f]{8}_[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant);

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ApiKeyEndpointTests(PostgresFixture postgres)
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

    /// <summary>An administrator creating a key gets 201 and the plaintext once, shaped as a key.</summary>
    [Fact]
    public async Task Creating_a_key_answers_201_with_a_plaintext_shaped_as_a_key()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();

        var response = await admin.PostAsJsonAsync(
            ApiKeyHost.ManagementRoute,
            new { Name = "ci deploy", Scopes = new[] { ApiKeyScopes.SelfRead } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var secret = body.RootElement.GetProperty("secret").GetString()!;
        Assert.Matches(KeyShape, secret);
        var key = body.RootElement.GetProperty("key");
        Assert.Equal("ci deploy", key.GetProperty("name").GetString());
        Assert.Equal("active", key.GetProperty("status").GetString());
        Assert.StartsWith("mk_" + key.GetProperty("prefix").GetString() + "_", secret, StringComparison.Ordinal);
    }

    /// <summary>The list shows a key's prefix and metadata, never the plaintext and never the stored digest.</summary>
    [Fact]
    public async Task Listing_keys_never_shows_the_plaintext_or_the_digest()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var issued = await host.CreateKeyAsync(admin, "ci deploy");
        var digest = (await host.ReadRowAsync(issued.Id)).TokenHash;

        var response = await admin.GetAsync(ApiKeyHost.ManagementRoute);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains(issued.Prefix, text, StringComparison.Ordinal);
        Assert.DoesNotContain(issued.Secret, text, StringComparison.Ordinal);
        Assert.DoesNotContain(digest, text, StringComparison.Ordinal);
        Assert.NotEqual(issued.Secret, digest);
    }

    /// <summary>An unknown scope is refused with its code and creates nothing.</summary>
    [Fact]
    public async Task Creating_a_key_with_an_unknown_scope_is_refused_with_its_code()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();

        var response = await admin.PostAsJsonAsync(
            ApiKeyHost.ManagementRoute,
            new { Name = "ci deploy", Scopes = new[] { "accounts.write" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ApiKeyScopeUnknown", await ProblemDocument.CodeAsync(response));
        Assert.Equal(0, (await ListAsync(admin, string.Empty)).Total);
    }

    /// <summary>The status list names active, revoked and expired in that order, as the filter spells them.</summary>
    [Fact]
    public async Task The_status_list_names_active_revoked_and_expired_in_that_order()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();

        var response = await admin.GetAsync($"{ApiKeyHost.ManagementRoute}/statuses");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var choices = body.RootElement.EnumerateArray().ToList();
        Assert.Equal(
            new[] { "active", "revoked", "expired" },
            choices.Select(choice => { return choice.GetProperty("value").GetString(); }));
        Assert.All(choices, choice =>
        {
            Assert.False(string.IsNullOrWhiteSpace(choice.GetProperty("name").GetString()));
            Assert.NotEqual(choice.GetProperty("value").GetString(), choice.GetProperty("name").GetString());
        });
    }

    /// <summary>The status filter separates active, revoked and expired keys.</summary>
    [Fact]
    public async Task Listing_filters_keys_by_status()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var firstAdmin = await host.SignInAdministratorAsync();
        await host.CreateKeyAsync(firstAdmin, "alpha-ci");
        var revoked = await host.CreateKeyAsync(firstAdmin, "beta-ci");
        await host.CreateKeyAsync(firstAdmin, "gamma-deploy", expiresAt: host.Clock.UtcNow.AddHours(1));
        await host.RevokeKeyAsync(firstAdmin, revoked.Id);
        host.Clock.Advance(TimeSpan.FromHours(2));
        using var admin = await host.SignInAdministratorAsync();

        Assert.Equal(new[] { "alpha-ci" }, (await ListAsync(admin, "?status=active")).Names);
        Assert.Equal(new[] { "beta-ci" }, (await ListAsync(admin, "?status=revoked")).Names);
        Assert.Equal(new[] { "gamma-deploy" }, (await ListAsync(admin, "?status=expired")).Names);
        Assert.Equal(3, (await ListAsync(admin, string.Empty)).Total);
    }

    /// <summary>An unknown status is refused with its code rather than answered with an empty page.</summary>
    [Fact]
    public async Task Listing_with_an_unknown_status_is_refused_with_its_code()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();

        var response = await admin.GetAsync($"{ApiKeyHost.ManagementRoute}?status=dormant");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ApiKeyStatusFilterInvalid", await ProblemDocument.CodeAsync(response));
    }

    /// <summary>The search matches a name fragment case-insensitively and answers nothing for a fragment no name holds.</summary>
    [Fact]
    public async Task Listing_searches_keys_by_name_fragment()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        await host.CreateKeyAsync(admin, "alpha-ci");
        await host.CreateKeyAsync(admin, "beta-CI");
        await host.CreateKeyAsync(admin, "gamma-deploy");

        Assert.Equal(new[] { "alpha-ci", "beta-CI" }, (await ListAsync(admin, "?search=ci&sort=name")).Names);
        Assert.Empty((await ListAsync(admin, "?search=nomatch")).Names);
    }

    /// <summary>The sort whitelist orders by name in both directions.</summary>
    [Fact]
    public async Task Listing_sorts_keys_by_name_in_both_directions()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        await host.CreateKeyAsync(admin, "beta");
        await host.CreateKeyAsync(admin, "gamma");
        await host.CreateKeyAsync(admin, "alpha");

        Assert.Equal(new[] { "alpha", "beta", "gamma" }, (await ListAsync(admin, "?sort=name")).Names);
        Assert.Equal(new[] { "gamma", "beta", "alpha" }, (await ListAsync(admin, "?sort=-name")).Names);
    }

    /// <summary>A second page holds the remainder, and the total counts every key.</summary>
    [Fact]
    public async Task Listing_pages_through_the_keys()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        for (var index = 1; index <= 11; index++)
        {
            await host.CreateKeyAsync(admin, $"key-{index:00}");
        }

        var first = await ListAsync(admin, "?page=1&pageSize=10&sort=name");
        var second = await ListAsync(admin, "?page=2&pageSize=10&sort=name");

        Assert.Equal(10, first.Names.Count);
        Assert.Equal(new[] { "key-11" }, second.Names);
        Assert.Equal(11, first.Total);
        Assert.Equal(11, second.Total);
    }

    /// <summary>Revoking answers 204, and a second revoke answers 204 without moving the revocation time.</summary>
    [Fact]
    public async Task Revoking_a_key_answers_204_and_a_second_revoke_is_idempotent()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var issued = await host.CreateKeyAsync(admin, "ci deploy");

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{ApiKeyHost.ManagementRoute}/{issued.Id}")).StatusCode);
        var firstRevokedAt = (await host.ReadRowAsync(issued.Id)).RevokedAt;
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{ApiKeyHost.ManagementRoute}/{issued.Id}")).StatusCode);

        Assert.NotNull(firstRevokedAt);
        Assert.Equal(firstRevokedAt, (await host.ReadRowAsync(issued.Id)).RevokedAt);
        Assert.Single(await host.ReadAuditAsync(IdentityAuditActions.ApiKeyRevoked));
    }

    /// <summary>Revoking a key that does not exist is 404 with its code.</summary>
    [Fact]
    public async Task Revoking_an_unknown_key_answers_404()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();

        var response = await admin.DeleteAsync($"{ApiKeyHost.ManagementRoute}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ApiKeyNotFound", await ProblemDocument.CodeAsync(response));
    }

    /// <summary>A customer login is refused every management action with 403.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The route.</param>
    [Theory]
    [InlineData("GET", "/api/v1/api-keys")]
    [InlineData("GET", "/api/v1/api-keys/scopes")]
    [InlineData("GET", "/api/v1/api-keys/statuses")]
    [InlineData("POST", "/api/v1/api-keys")]
    [InlineData("DELETE", "/api/v1/api-keys/6f9619ff-8b86-d011-b42d-00c04fc964ff")]
    public async Task A_customer_is_refused_every_key_management_action(string method, string path)
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var customer = await EndpointHarness.SignInAsync(host.Factory, ApiKeyHost.CustomerName);

        var response = await customer.SendAsync(Request(method, path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A caller with no token is refused every management action with 401.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The route.</param>
    [Theory]
    [InlineData("GET", "/api/v1/api-keys")]
    [InlineData("GET", "/api/v1/api-keys/scopes")]
    [InlineData("GET", "/api/v1/api-keys/statuses")]
    [InlineData("POST", "/api/v1/api-keys")]
    [InlineData("DELETE", "/api/v1/api-keys/6f9619ff-8b86-d011-b42d-00c04fc964ff")]
    public async Task An_anonymous_caller_is_refused_every_key_management_action(string method, string path)
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var anonymous = host.Factory.CreateClient();

        var response = await anonymous.SendAsync(Request(method, path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>An operator viewing as a login cannot create or revoke a key, and nothing is stored.</summary>
    /// <param name="method">The HTTP method.</param>
    [Theory]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task A_view_as_operator_is_refused_creating_and_revoking_keys(string method)
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var existing = await host.CreateKeyAsync(admin, "ci deploy");
        using var viewing = await StartViewingAsync(host, admin);
        var path = method == "DELETE" ? $"{ApiKeyHost.ManagementRoute}/{existing.Id}" : ApiKeyHost.ManagementRoute;

        var response = await viewing.SendAsync(Request(method, path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("HostViewAsNotAllowed", await ProblemDocument.CodeAsync(response));
        Assert.Null((await host.ReadRowAsync(existing.Id)).RevokedAt);
        Assert.Equal(1, (await ListAsync(admin, string.Empty)).Total);
    }

    /// <summary>Creating and revoking are journalled with name, prefix and scopes and with no secret.</summary>
    [Fact]
    public async Task Creating_and_revoking_a_key_are_journalled_without_any_secret()
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        var issued = await host.CreateKeyAsync(admin, "ci deploy", [ApiKeyScopes.SelfRead, ApiKeyScopes.ServerRead]);
        await host.RevokeKeyAsync(admin, issued.Id);
        var digest = (await host.ReadRowAsync(issued.Id)).TokenHash;

        var created = Assert.Single(await host.ReadAuditAsync(IdentityAuditActions.ApiKeyCreated));
        var revoked = Assert.Single(await host.ReadAuditAsync(IdentityAuditActions.ApiKeyRevoked));

        foreach (var entry in new[] { created, revoked })
        {
            Assert.Contains(issued.Prefix, entry.Subject, StringComparison.Ordinal);
            Assert.Contains("ci deploy", entry.Subject, StringComparison.Ordinal);
            Assert.Contains(ApiKeyScopes.ServerRead, entry.Subject, StringComparison.Ordinal);
            Assert.True(entry.Succeeded);
            var columns = string.Join('\n', entry.ActorUsername, entry.Action, entry.Subject, entry.IpAddress, entry.UserAgent, entry.CorrelationId);
            Assert.DoesNotContain(issued.Secret, columns, StringComparison.Ordinal);
            Assert.DoesNotContain(digest, columns, StringComparison.Ordinal);
        }
    }

    /// <summary>The scope choices come from the backend in the caller's language.</summary>
    /// <param name="language">The <c>Accept-Language</c> the caller sends.</param>
    /// <param name="selfRead">The expected name of the self-read scope.</param>
    /// <param name="serverRead">The expected name of the server-read scope.</param>
    [Theory]
    [InlineData("en", "Read the key's own details", "Read server information")]
    [InlineData("ru", "Чтение сведений о самом ключе", "Чтение сведений о сервере")]
    [InlineData("hy", "Բանալու սեփական տվյալների ընթերցում", "Սերվերի տվյալների ընթերցում")]
    public async Task The_scope_choices_are_localized(string language, string selfRead, string serverRead)
    {
        await using var host = await ApiKeyHost.BootAsync(_pg);
        using var admin = await host.SignInAdministratorAsync();
        admin.DefaultRequestHeaders.Add("Accept-Language", language);

        var response = await admin.GetAsync($"{ApiKeyHost.ManagementRoute}/scopes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.EnumerateArray().ToDictionary(
            choice => choice.GetProperty("value").GetString()!,
            choice => choice.GetProperty("name").GetString()!);
        Assert.Equal(selfRead, names[ApiKeyScopes.SelfRead]);
        Assert.Equal(serverRead, names[ApiKeyScopes.ServerRead]);
        Assert.Equal(ApiKeyScopes.All.Count, names.Count);
    }

    /// <summary>Builds a request with an empty JSON body, which the actions that read none ignore.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The route.</param>
    /// <returns>The request.</returns>
    private static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { Name = "x", Scopes = new[] { ApiKeyScopes.SelfRead } });
        }

        return request;
    }

    /// <summary>Lists keys through the management endpoint and reads the names and the total.</summary>
    /// <param name="admin">The administrator's client.</param>
    /// <param name="query">The query string, with its leading question mark, or empty.</param>
    /// <returns>The names in the order answered, and the total over all pages.</returns>
    private static async Task<(IReadOnlyList<string> Names, int Total)> ListAsync(HttpClient admin, string query)
    {
        var response = await admin.GetAsync(ApiKeyHost.ManagementRoute + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var names = body.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("name").GetString()!)
            .ToList();

        return (names, body.RootElement.GetProperty("total").GetInt32());
    }

    /// <summary>Starts viewing as the seeded account and returns a client carrying the view-as token.</summary>
    /// <param name="host">The booted host.</param>
    /// <param name="admin">The administrator's client.</param>
    /// <returns>A client the caller disposes.</returns>
    private static async Task<HttpClient> StartViewingAsync(ApiKeyHost host, HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/view-as", new { AccountId = host.AccountId });
        response.EnsureSuccessStatusCode();

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var client = host.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            body.RootElement.GetProperty("accessToken").GetString()!);

        return client;
    }
}
