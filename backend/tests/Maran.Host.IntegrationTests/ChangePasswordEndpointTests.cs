using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Host.Middleware;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>POST /api/v1/auth/change-password</c> over real HTTP: the wrong current password is refused
/// with its code, the policy is enforced, the change takes effect at sign-in, every other session is
/// ended while the caller's own survives, the change is journalled and attempts are rate limited.
/// Threat note: docs/superpowers/notes/2026-10-31-change-password-threat-note.md.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: access tokens already issued to a revoked session stay valid until they expire;
/// these tests observe revocation through the refresh endpoint, which is where it takes effect.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ChangePasswordEndpointTests : IAsyncLifetime
{
    /// <summary>The replacement every successful test sets.</summary>
    private const string NewPassword = "a brand new passphrase";

    /// <summary>The endpoint under test.</summary>
    private const string ChangePasswordPath = "/api/v1/auth/change-password";

    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>The journal the host writes to, read back by the assertions.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ChangePasswordEndpointTests(PostgresFixture postgres)
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

    /// <summary>Boots a migrated panel with one administrator and a recording journal.</summary>
    /// <returns>The factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(_pg, services =>
        {
            services.AddSingleton<IAuditWriter>(_audit);
        });
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        return factory;
    }

    /// <summary>Signs the administrator in, returning a browser-like client and its refresh token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <returns>A client carrying the access token, the refresh cookie and the request header.</returns>
    private static async Task<(HttpClient Client, string RefreshToken)> SignInAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { Username = "admin", Password = EndpointHarness.Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        var refreshToken = response.Headers.GetValues("Set-Cookie").Single().Split(';')[0].Split('=', 2)[1];

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("Cookie", $"maran_refresh={refreshToken}");
        client.DefaultRequestHeaders.Add(CsrfHeaderMiddleware.HeaderName, "1");

        return (client, refreshToken);
    }

    /// <summary>Posts a change.</summary>
    /// <param name="client">The signed-in client.</param>
    /// <param name="currentPassword">What the caller claims is their current password.</param>
    /// <param name="newPassword">The replacement.</param>
    /// <returns>The response.</returns>
    private static Task<HttpResponseMessage> ChangeAsync(
        HttpClient client,
        string currentPassword,
        string newPassword = NewPassword)
    {
        return client.PostAsJsonAsync(ChangePasswordPath, new { CurrentPassword = currentPassword, NewPassword = newPassword });
    }

    /// <summary>Reads the problem code from a refusal.</summary>
    /// <param name="response">The refusal.</param>
    /// <returns>The code.</returns>
    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString()!;
    }

    /// <summary>Tries a sign-in with the given password.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="password">The password to try.</param>
    /// <returns>The status the sign-in answered.</returns>
    private static async Task<HttpStatusCode> LoginStatusAsync(WebApplicationFactory<Program> factory, string password)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = "admin", Password = password });
        return response.StatusCode;
    }

    /// <summary>A wrong current password is refused with its classified code.</summary>
    [Fact]
    public async Task A_wrong_current_password_is_refused_with_its_classified_code()
    {
        await using var factory = await BootAsync();
        var (client, _) = await SignInAsync(factory);

        var response = await ChangeAsync(client, "not the password");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("CurrentPasswordIncorrect", await CodeOfAsync(response));
    }

    /// <summary>A new password shorter than the policy minimum is refused as too weak.</summary>
    [Fact]
    public async Task A_new_password_shorter_than_the_policy_minimum_is_refused_as_too_weak()
    {
        await using var factory = await BootAsync();
        var (client, _) = await SignInAsync(factory);

        var response = await ChangeAsync(client, EndpointHarness.Password, "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("PasswordTooWeak", await CodeOfAsync(response));
    }

    /// <summary>After a change the new password signs in and the old one does not.</summary>
    [Fact]
    public async Task After_a_change_the_new_password_signs_in_and_the_old_one_does_not()
    {
        await using var factory = await BootAsync();
        var (client, _) = await SignInAsync(factory);

        var response = await ChangeAsync(client, EndpointHarness.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(factory, NewPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(factory, EndpointHarness.Password));
    }

    /// <summary>Another session is ended by the change and the caller's own still refreshes.</summary>
    [Fact]
    public async Task Another_session_is_ended_by_the_change_and_the_callers_own_still_refreshes()
    {
        await using var factory = await BootAsync();
        var (other, _) = await SignInAsync(factory);
        var (caller, _) = await SignInAsync(factory);

        await ChangeAsync(caller, EndpointHarness.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, (await other.PostAsync("/api/v1/auth/refresh", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await caller.PostAsync("/api/v1/auth/refresh", content: null)).StatusCode);
    }

    /// <summary>A change is journalled as the caller's successful password change.</summary>
    [Fact]
    public async Task A_change_is_journalled_as_the_callers_successful_password_change()
    {
        await using var factory = await BootAsync();
        var (client, _) = await SignInAsync(factory);

        await ChangeAsync(client, EndpointHarness.Password);

        var entry = Assert.Single(_audit.Entries, candidate =>
        {
            return candidate.Action == AuditActions.PasswordChanged;
        });
        Assert.True(entry.Succeeded);
        Assert.Equal("admin", entry.ActorUsername);
        Assert.DoesNotContain(NewPassword, entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>Attempts beyond the sign-in budget are refused with 429.</summary>
    /// <remarks>
    /// The endpoint shares the sign-in limiter's per-address budget, which the sign-in itself has
    /// already spent one of. The budget is configured down to three attempts in one window, the way
    /// the reset tests configure theirs. The mutation that removes the rate-limit attribute must
    /// turn this red.
    /// </remarks>
    [Fact]
    public async Task Attempts_beyond_the_sign_in_budget_are_refused_with_too_many_requests()
    {
        await using var booted = await BootAsync();
        await using var factory = booted.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:LoginMaxAttempts", "3");
            builder.UseSetting("RateLimiting:LoginWindowSeconds", "60");
            builder.UseSetting("RateLimiting:LoginLockoutSeconds", "60");
        });
        var (client, _) = await SignInAsync(factory);

        var second = await ChangeAsync(client, "not the password");
        var third = await ChangeAsync(client, "not the password");
        var fourth = await ChangeAsync(client, "not the password");

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, third.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.StatusCode);
    }

    /// <summary>An anonymous caller is refused before anything is journalled.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused_before_anything_is_journalled()
    {
        await using var factory = await BootAsync();
        using var client = factory.CreateClient();

        var response = await ChangeAsync(client, EndpointHarness.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(_audit.Entries, entry =>
        {
            return entry.Action == AuditActions.PasswordChanged;
        });
    }
}
