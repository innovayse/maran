using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Modules.Identity.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// First-run setup over real HTTP. This is also the one place that proves the command validators
/// actually run: they are invoked by Wolverine middleware, not by any code a unit test calls, so a
/// validator can be written, tested and registered and still never execute — which is exactly what
/// happened until a weak password reached the database.
/// </summary>
public sealed class SetupEndpointTests : IAsyncLifetime
{
    private const string Token = "a-one-time-token";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private const string Password = "correct horse battery staple";

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
            builder.UseSetting("Setup:Token", Token);
        });
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    private static Task<HttpResponseMessage> SetupAsync(
        HttpClient client,
        string token = Token,
        string username = "admin",
        string email = "admin@example.com",
        string password = Password)
    {
        return client.PostAsJsonAsync("/api/v1/setup", new { Token = token, Username = username, Email = email, Password = password });
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>An empty panel reports that setup is not complete.</summary>
    [Fact]
    public async Task An_empty_panel_reports_that_setup_is_not_complete()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/setup/state");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("isComplete").GetBoolean());
    }

    /// <summary>Completing setup creates an administrator who can then sign in.</summary>
    [Fact]
    public async Task Completing_setup_creates_an_administrator_who_can_then_sign_in()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var setup = await SetupAsync(client);

        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { Username = "admin", Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    /// <summary>A weak password is refused with the rule that was broken.</summary>
    [Fact]
    public async Task A_weak_password_is_refused_with_the_rule_that_was_broken()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SetupAsync(client, password: "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("PasswordTooWeak", await CodeOf(response));
    }

    /// <summary>A username that is not a valid login name is refused.</summary>
    [Fact]
    public async Task A_username_that_is_not_a_valid_login_name_is_refused()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SetupAsync(client, username: "the admin");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("UsernameInvalidFormat", await CodeOf(response));
    }

    /// <summary>A wrong token is refused and creates nobody.</summary>
    [Fact]
    public async Task A_wrong_token_is_refused_and_creates_nobody()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SetupAsync(client, token: "not-the-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("SetupTokenInvalidUnauthorized", await CodeOf(response));
        using var state = JsonDocument.Parse(await (await client.GetAsync("/api/v1/setup/state")).Content.ReadAsStringAsync());
        Assert.False(state.RootElement.GetProperty("isComplete").GetBoolean());
    }

    /// <summary>Setup cannot be run a second time even with the right token.</summary>
    [Fact]
    public async Task Setup_cannot_be_run_a_second_time_even_with_the_right_token()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();
        await SetupAsync(client);

        var second = await SetupAsync(client, username: "second", email: "second@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        Assert.Equal("SetupAlreadyCompletedForbidden", await CodeOf(second));
    }
}
