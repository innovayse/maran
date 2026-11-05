using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Interfaces;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Utilities.Tokens;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>GET /api/v1/server/status</c> over real HTTP against real PostgreSQL: the one endpoint that
/// admits an API key holding the <c>server.read</c> scope (issue #144), and who it refuses.
/// </summary>
/// <remarks>
/// <para>
/// Keys are minted with the production <see cref="ApiKeyHasher"/> and issued through
/// <see cref="ApiKey.Issue"/>, then seeded straight into the Identity schema, so the request travels
/// the real authentication handler, the real authenticator and the real policy. The agent is the only
/// substitution, because it is a separate root process.
/// </para>
/// <para>
/// An administrator's bearer token is answered 401, not 403: the endpoint authenticates with the
/// <c>ApiKey</c> scheme only, so a bearer token is no credential here at all. 403 is reserved for a
/// valid key that lacks the scope.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ServerStatusEndpointTests : IAsyncLifetime
{
    /// <summary>A well-known development key; the host refuses to boot without one.</summary>
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>The password the seeded administrator is given.</summary>
    private const string Password = "correct horse battery staple";

    /// <summary>The route under test.</summary>
    private const string Route = "/api/v1/server/status";

    /// <summary>The PostgreSQL this class boots the host against.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ServerStatusEndpointTests(PostgresFixture postgres)
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

    /// <summary>A request carrying no credential is answered 401.</summary>
    [Fact]
    public async Task A_request_with_no_credential_is_answered_401()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A key of the right shape that no row holds is answered 401.</summary>
    [Fact]
    public async Task A_key_no_row_holds_is_answered_401()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var unknown = ApiKeyHasher.Mint().Plaintext;

        using var client = ClientWithKey(factory, unknown);
        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A valid key that holds only the implicit self scope is answered 403.</summary>
    [Fact]
    public async Task A_valid_key_without_the_server_scope_is_answered_403()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var plaintext = await SeedKeyAsync(factory, [ApiKeyScopes.SelfRead], revoke: false);

        using var client = ClientWithKey(factory, plaintext);
        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A key holding the server scope reads the stub agent's figures.</summary>
    [Fact]
    public async Task A_key_with_the_server_scope_reads_the_agents_figures()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var plaintext = await SeedKeyAsync(factory, [ApiKeyScopes.ServerRead], revoke: false);

        using var client = ClientWithKey(factory, plaintext);
        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("pendingUpdateCount").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("securityUpdateCount").GetInt32());
        Assert.True(body.RootElement.GetProperty("rebootRequired").GetBoolean());
    }

    /// <summary>A revoked key is answered 401 although it held the scope.</summary>
    [Fact]
    public async Task A_revoked_key_is_answered_401()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var plaintext = await SeedKeyAsync(factory, [ApiKeyScopes.ServerRead], revoke: true);

        using var client = ClientWithKey(factory, plaintext);
        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>An administrator's bearer token is no credential for this endpoint and is answered 401.</summary>
    [Fact]
    public async Task An_administrators_bearer_token_is_answered_401()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedAdministratorAsync(factory);
        using var client = await SignInAsAdministratorAsync(factory);

        var response = await client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>The ordinary administrator route is not reachable with a key, so the opt-in is not a widening.</summary>
    [Fact]
    public async Task A_key_with_the_server_scope_is_refused_by_the_administrator_route()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        var plaintext = await SeedKeyAsync(factory, [ApiKeyScopes.ServerRead], revoke: false);

        using var client = ClientWithKey(factory, plaintext);
        var response = await client.GetAsync("/api/v1/monitoring/updates/count");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Boots the host against this class's PostgreSQL, with the agent replaced.</summary>
    /// <returns>The booted host factory.</returns>
    private WebApplicationFactory<Program> CreateFactory()
    {
        var agent = new StubAgentMonitorClient
        {
            PendingUpdates = Result<IReadOnlyList<AgentPendingUpdate>>.Ok(
            [
                new AgentPendingUpdate("bash", "2", "1", "updates", AgentUpdateClassification.Ordinary),
                new AgentPendingUpdate("openssl", "2", "1", "security", AgentUpdateClassification.Security),
            ]),
            RebootRequirement = Result<AgentRebootRequirement>.Ok(AgentRebootRequirement.Required),
        };

        return new ConfiguredHostFactory(builder =>
        {
            builder.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.UseSetting("Security:EncryptionKey", Key);
            builder.UseSetting("Jwt:SigningKey", Key);

            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IAgentMonitorClient>(agent);
            });
        });
    }

    /// <summary>Applies the Identity migrations, which hold the users and the keys.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    /// <summary>Mints a key with the production minter and stores its row directly.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="scopes">The scopes the key is granted.</param>
    /// <param name="revoke">Whether the key is revoked before the test uses it.</param>
    /// <returns>The plaintext key, which only the test ever holds.</returns>
    private static async Task<string> SeedKeyAsync(
        WebApplicationFactory<Program> factory,
        string[] scopes,
        bool revoke)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        var minted = ApiKeyHasher.Mint();
        var key = ApiKey.Issue(
            Guid.NewGuid(), "integration test", minted.Prefix, minted.Digest, scopes, null, Guid.NewGuid(), now, null);
        if (revoke)
        {
            key.Revoke(now);
        }

        identity.ApiKeys.Add(key);
        await identity.SaveChangesAsync();

        return minted.Plaintext;
    }

    /// <summary>Creates a client that presents a key in the key header.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="plaintext">The key to present.</param>
    /// <returns>The client.</returns>
    private static HttpClient ClientWithKey(WebApplicationFactory<Program> factory, string plaintext)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(ApiKeyDefaults.HeaderName, plaintext);

        return client;
    }

    /// <summary>Seeds one administrator.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedAdministratorAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));

        await identity.SaveChangesAsync();
    }

    /// <summary>Signs the administrator in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>A client whose requests carry the administrator's bearer token.</returns>
    private static async Task<HttpClient> SignInAsAdministratorAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = "admin", Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }
}
