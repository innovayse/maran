using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Commands.Login;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The sign-in endpoint over real HTTP against a real database. The handler's own tests cover the
/// decisions; what only this level can show is what actually crosses the wire — the status, the
/// body, and above all the Set-Cookie attributes, which no unit test can observe.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class AuthEndpointTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AuthEndpointTests(PostgresFixture postgres)
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

            // Startup validation refuses to boot without the host's SSH ports and the panel's
            // public port: a defaulted one is a locked-out server (rules/security.md).
            foreach (var setting in FirewallSettings.Required())
            {
                builder.UseSetting(setting.Key, setting.Value);
            }
        });
    }

    private static async Task SeedAdministratorAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await context.Database.MigrateAsync();

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        context.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, clock.UtcNow));
        await context.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string password)
    {
        return await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { Username = "admin", Password = password });
    }

    /// <summary>
    /// Creates a customer login holding a VALID hash of <see cref="Password"/>, then moves it out of
    /// <see cref="UserState.Active"/> the only way the domain allows: <see cref="User.Suspend"/>,
    /// which deliberately keeps the password hash rather than clearing it (its own remarks). An
    /// invited login's empty hash would fail password verification on its own and prove nothing
    /// about the state check that runs after verification; a suspended login is the one state whose
    /// password is real and still must be refused.
    /// </summary>
    private static async Task SeedSuspendedCustomerAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await context.Database.MigrateAsync();

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var user = new User(
            Guid.NewGuid(), "suspended-owner", "suspended-owner@example.com", hasher.Hash(Password),
            UserRole.Customer, clock.UtcNow);
        user.Activate(hasher.Hash(Password));
        user.Suspend();
        context.Users.Add(user);
        await context.SaveChangesAsync();
    }

    /// <summary>Strips the per-request correlation id, which is not part of what an answer says.</summary>
    /// <param name="body">The response body.</param>
    /// <returns>The body with the correlation id replaced by a fixed marker.</returns>
    private static string WithoutCorrelationId(string body)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            body,
            "\"correlationId\":\"[^\"]*\"",
            "\"correlationId\":\"-\"",
            System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromSeconds(1));
    }

    /// <summary>Signing in with the right password returns an access token.</summary>
    [Fact]
    public async Task Signing_in_with_the_right_password_returns_an_access_token()
    {
        await using var factory = CreateFactory();
        await SeedAdministratorAsync(factory);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()));
        Assert.Equal("admin", body.RootElement.GetProperty("session").GetProperty("user").GetProperty("username").GetString());
    }

    /// <summary>The refresh cookie is http only secure and same site strict.</summary>
    [Fact]
    public async Task The_refresh_cookie_is_http_only_secure_and_same_site_strict()
    {
        await using var factory = CreateFactory();
        await SeedAdministratorAsync(factory);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, Password);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("maran_refresh=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The response body never carries the refresh token.</summary>
    [Fact]
    public async Task The_response_body_never_carries_the_refresh_token()
    {
        await using var factory = CreateFactory();
        await SeedAdministratorAsync(factory);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, Password);

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        var token = cookie.Split(';')[0].Split('=', 2)[1];
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>Signing in with a wrong password returns 401 and sets no cookie.</summary>
    [Fact]
    public async Task Signing_in_with_a_wrong_password_returns_401_and_sets_no_cookie()
    {
        await using var factory = CreateFactory();
        await SeedAdministratorAsync(factory);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, "wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    /// <summary>
    /// A login in a non-Active state, holding a VALID password hash, is refused identically to a
    /// wrong password — not merely with the same status, but with the same body. Without a state
    /// check, this exact request would succeed: the hash really does verify. The existing coverage
    /// of a refused invited login is not this proof, because its empty password hash fails
    /// verification regardless of whether a state check exists at all (<see cref="LoginCommandHandler"/>'s
    /// own remarks); this test seeds a login whose password is real so the state gate is the only
    /// thing standing between the correct password and a session.
    /// </summary>
    [Fact]
    public async Task A_suspended_login_with_the_correct_password_is_refused_exactly_like_a_wrong_one()
    {
        await using var factory = CreateFactory();
        await SeedSuspendedCustomerAsync(factory);
        using var correctClient = factory.CreateClient();
        using var wrongClient = factory.CreateClient();

        var correctPasswordResponse = await correctClient.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "suspended-owner", Password });
        var wrongPasswordResponse = await wrongClient.PostAsJsonAsync(
            "/api/v1/auth/login", new { Username = "suspended-owner", Password = "wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, correctPasswordResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPasswordResponse.StatusCode);
        Assert.False(correctPasswordResponse.Headers.Contains("Set-Cookie"));

        var correctBody = WithoutCorrelationId(await correctPasswordResponse.Content.ReadAsStringAsync());
        var wrongBody = WithoutCorrelationId(await wrongPasswordResponse.Content.ReadAsStringAsync());
        Assert.Equal(wrongBody, correctBody);
    }

    /// <summary>A refused sign in says the same thing in the users own language.</summary>
    [Fact]
    public async Task A_refused_sign_in_says_the_same_thing_in_the_users_own_language()
    {
        await using var factory = CreateFactory();
        await SeedAdministratorAsync(factory);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "ru");

        var response = await LoginAsync(client, "wrong");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("InvalidCredentialsUnauthorized", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("Неверное имя пользователя или пароль.", body.RootElement.GetProperty("detail").GetString());
    }
}
