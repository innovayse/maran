using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.Middleware;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The refresh, sign-out and session-listing endpoints over real HTTP. These are the flows whose
/// whole behaviour lives in headers and cookies — rotation, deletion, and who may see what — so
/// they can only be shown at this level.
/// </summary>
public sealed class SessionEndpointTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.StartAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return _pg.DisposeAsync().AsTask();
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
        });
    }

    private static async Task SeedAsync(WebApplicationFactory<Program> factory, params string[] usernames)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await context.Database.MigrateAsync();

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        foreach (var username in usernames)
        {
            context.Users.Add(new User(
                Guid.NewGuid(), username, $"{username}@example.com", hasher.Hash(Password), UserRole.Admin, clock.UtcNow));
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Signs in and returns a client carrying both the access token and the refresh cookie.</summary>
    private static async Task<(HttpClient Client, string AccessToken, string RefreshToken)> SignInAsync(
        WebApplicationFactory<Program> factory,
        string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/auth/login",
            new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString()!;
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        var refreshToken = cookie.Split(';')[0].Split('=', 2)[1];

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("Cookie", $"maran_refresh={refreshToken}");

        // The header the SPA sets on every request. A cookie-bearing state change without it is
        // refused by CsrfHeaderMiddleware — which is the point of the middleware, so a client that
        // stands in for the browser has to behave like one.
        client.DefaultRequestHeaders.Add(CsrfHeaderMiddleware.HeaderName, "1");

        return (client, accessToken, refreshToken);
    }

    /// <summary>Refreshing returns a new access token and a new cookie.</summary>
    [Fact]
    public async Task Refreshing_returns_a_new_access_token_and_a_new_cookie()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory, "admin");
        var (client, accessToken, refreshToken) = await SignInAsync(factory, "admin");

        var response = await client.PostAsync("/api/v1/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotEqual(accessToken, body.RootElement.GetProperty("accessToken").GetString());
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.DoesNotContain(refreshToken, cookie, StringComparison.Ordinal);
    }

    /// <summary>Refreshing with no cookie at all returns 401 rather than 500.</summary>
    [Fact]
    public async Task Refreshing_with_no_cookie_at_all_returns_401_rather_than_500()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory, "admin");
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Presenting a spent refresh cookie a second time is refused.</summary>
    [Fact]
    public async Task Presenting_a_spent_refresh_cookie_a_second_time_is_refused()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory, "admin");
        var (client, _, refreshToken) = await SignInAsync(factory, "admin");
        await client.PostAsync("/api/v1/auth/refresh", content: null);

        using var replayClient = factory.CreateClient();
        replayClient.DefaultRequestHeaders.Add("Cookie", $"maran_refresh={refreshToken}");
        replayClient.DefaultRequestHeaders.Add(CsrfHeaderMiddleware.HeaderName, "1");
        var response = await replayClient.PostAsync("/api/v1/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("RefreshTokenReusedUnauthorized", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>Listing sessions without a token is refused.</summary>
    [Fact]
    public async Task Listing_sessions_without_a_token_is_refused()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory, "admin");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/sessions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Listing sessions returns the callers own and marks the current one.</summary>
    [Fact]
    public async Task Listing_sessions_returns_the_callers_own_and_marks_the_current_one()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory, "admin");
        var (client, _, _) = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/sessions");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var session = Assert.Single(body.RootElement.EnumerateArray());
        Assert.True(session.GetProperty("isCurrent").GetBoolean());
    }

    /// <summary>A listed session never carries a token or a hash of one.</summary>
    [Fact]
    public async Task A_listed_session_never_carries_a_token_or_a_hash_of_one()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory, "admin");
        var (client, _, refreshToken) = await SignInAsync(factory, "admin");

        var payload = await (await client.GetAsync("/api/v1/sessions")).Content.ReadAsStringAsync();

        Assert.DoesNotContain(refreshToken, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("tokenHash", payload, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Revoking another users session answers 404 rather than 403.</summary>
    [Fact]
    public async Task Revoking_another_users_session_answers_404_rather_than_403()
    {
        // The IDOR test rules/testing.md requires on every tenant-scoped endpoint.
        await using var factory = CreateFactory();
        await SeedAsync(factory, "alice", "bob");
        var (aliceClient, _, _) = await SignInAsync(factory, "alice");
        var (bobClient, _, _) = await SignInAsync(factory, "bob");

        var bobSessions = JsonDocument.Parse(await (await bobClient.GetAsync("/api/v1/sessions")).Content.ReadAsStringAsync());
        var bobSessionId = bobSessions.RootElement.EnumerateArray().Single().GetProperty("id").GetString();

        var response = await aliceClient.DeleteAsync($"/api/v1/sessions/{bobSessionId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Signing out clears the cookie and ends the session.</summary>
    [Fact]
    public async Task Signing_out_clears_the_cookie_and_ends_the_session()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory, "admin");
        var (client, _, refreshToken) = await SignInAsync(factory, "admin");

        var response = await client.PostAsync("/api/v1/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.StartsWith("maran_refresh=;", cookie, StringComparison.Ordinal);

        using var replay = factory.CreateClient();
        replay.DefaultRequestHeaders.Add("Cookie", $"maran_refresh={refreshToken}");
        replay.DefaultRequestHeaders.Add(CsrfHeaderMiddleware.HeaderName, "1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.PostAsync("/api/v1/auth/refresh", content: null)).StatusCode);
    }

    /// <summary>An access token issued before a sign out stays valid until it expires.</summary>
    [Fact]
    public async Task An_access_token_issued_before_a_sign_out_stays_valid_until_it_expires()
    {
        // Not a defect: an access token is verified by signature alone, so ending a session cannot
        // reach back and cancel one already handed out. What ending the session does is stop the
        // chain — no refresh, so the outstanding token is the last one, and it dies within fifteen
        // minutes (spec §10). Checking the session on every request would close the window at the
        // cost of a database round trip per API call; the short lifetime is the chosen trade, and
        // this test is where it is written down rather than rediscovered.
        await using var factory = CreateFactory();
        await SeedAsync(factory, "admin");
        var (client, _, _) = await SignInAsync(factory, "admin");
        await client.PostAsync("/api/v1/auth/logout", content: null);

        var response = await client.GetAsync("/api/v1/sessions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.EnumerateArray());
    }

    /// <summary>Signing out everywhere requires a token.</summary>
    [Fact]
    public async Task Signing_out_everywhere_requires_a_token()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory, "admin");
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/logout-all", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Signing out everywhere ends every session of the user.</summary>
    [Fact]
    public async Task Signing_out_everywhere_ends_every_session_of_the_user()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory, "admin");
        var (first, _, _) = await SignInAsync(factory, "admin");
        var (second, _, secondRefresh) = await SignInAsync(factory, "admin");

        await first.PostAsync("/api/v1/auth/logout-all", content: null);

        using var replay = factory.CreateClient();
        replay.DefaultRequestHeaders.Add("Cookie", $"maran_refresh={secondRefresh}");
        replay.DefaultRequestHeaders.Add(CsrfHeaderMiddleware.HeaderName, "1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.PostAsync("/api/v1/auth/refresh", content: null)).StatusCode);
        second.Dispose();
    }
}
