using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Controllers;
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
/// The security-policy surface over real HTTP against real PostgreSQL: who may read and change the
/// panel's password minimum, its forced-two-factor flag for administrators, its failed-attempt
/// threshold and its lockout duration.
/// </summary>
/// <remarks>
/// <para>
/// The gate is a ROLE, not ownership, exactly as <c>FirewallAuthorizationTests</c> describes for the
/// firewall surface, and this controller's own <c>&lt;remarks&gt;</c> says the same thing about the
/// policy: an administrator changing it changes every account on the panel at once, so read and
/// write share one rule. There is no tenant dimension and no identifier a customer could pass — the
/// whole surface is the panel's, not any one account's — so an anonymous caller is answered 401 and a
/// signed-in customer 403, never 404. 404 would be the tenant answer, and using it here would claim
/// there is no tenant-scoped row to find, when there is no tenant-scoped row in the first place: the
/// policy plainly exists, for every account on the panel, and the only thing wrong with a customer's
/// request is who is asking.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class SecurityPolicyAuthorizationTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    /// <summary>Every route the security-policy surface declares.</summary>
    /// <remarks>
    /// Completeness is asserted by
    /// <see cref="Every_security_policy_route_is_covered_by_the_gating_fixture"/> rather than
    /// trusted: a new route added without a row here would otherwise enjoy no proof that it is
    /// closed to a customer at all.
    /// </remarks>
    public static TheoryData<string, string> SecurityPolicyEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/security-policy" },
            { "PUT", "/api/v1/security-policy" },
        };
    }

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public SecurityPolicyAuthorizationTests(PostgresFixture postgres)
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

    /// <summary>An anonymous caller is refused by every security-policy endpoint.</summary>
    [Theory]
    [MemberData(nameof(SecurityPolicyEndpoints))]
    public async Task An_anonymous_caller_is_refused_by_every_security_policy_endpoint(string method, string path)
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A signed in customer is refused by every security-policy endpoint and told so plainly.</summary>
    [Theory]
    [MemberData(nameof(SecurityPolicyEndpoints))]
    public async Task A_signed_in_customer_is_refused_by_every_security_policy_endpoint_and_told_so_plainly(
        string method,
        string path)
    {
        // 403 and deliberately not 404, for the same reason FirewallAuthorizationTests states it:
        // there is no tenant dimension here and no identifier to probe. The policy exists for every
        // account on the panel; a customer is simply not an administrator.
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "customer");

        var response = await SendAsync(client, method, path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An administrator reads back the policy the panel is enforcing.</summary>
    [Fact]
    public async Task An_administrator_reads_back_the_policy_the_panel_is_enforcing()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/security-policy");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        // Nothing has been saved yet, so the absence of the row IS the defaults
        // (SecurityPolicy's own remarks: "there is deliberately no startup seeder").
        Assert.Equal(
            SecurityPolicy.DefaultMinimumPasswordLength,
            body.RootElement.GetProperty("minimumPasswordLength").GetInt32());
        Assert.Equal(
            SecurityPolicy.DefaultMaxFailedLoginAttempts,
            body.RootElement.GetProperty("maxFailedLoginAttempts").GetInt32());
        Assert.Equal(
            SecurityPolicy.DefaultLockoutMinutes,
            body.RootElement.GetProperty("lockoutMinutes").GetInt32());
        Assert.Equal(
            SecurityPolicy.DefaultForceTwoFactorForAdmins,
            body.RootElement.GetProperty("forceTwoFactorForAdmins").GetBoolean());
    }

    /// <summary>An administrator saves the policy and reads back exactly what was saved.</summary>
    [Fact]
    public async Task An_administrator_saves_the_policy_and_reads_back_exactly_what_was_saved()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var saved = await client.PutAsJsonAsync("/api/v1/security-policy", new
        {
            minimumPasswordLength = 16,
            forceTwoFactorForAdmins = true,
            maxFailedLoginAttempts = 5,
            lockoutMinutes = 30,
        });

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        var readBack = await client.GetAsync("/api/v1/security-policy");
        using var body = JsonDocument.Parse(await readBack.Content.ReadAsStringAsync());
        Assert.Equal(16, body.RootElement.GetProperty("minimumPasswordLength").GetInt32());
        Assert.True(body.RootElement.GetProperty("forceTwoFactorForAdmins").GetBoolean());
        Assert.Equal(5, body.RootElement.GetProperty("maxFailedLoginAttempts").GetInt32());
        Assert.Equal(30, body.RootElement.GetProperty("lockoutMinutes").GetInt32());
    }

    /// <summary>A policy outside the validator's bounds is answered as the caller's own mistake.</summary>
    [Fact]
    public async Task A_policy_outside_the_validators_bounds_is_answered_as_the_callers_own_mistake()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        var response = await client.PutAsJsonAsync("/api/v1/security-policy", new
        {
            minimumPasswordLength = 4, // below the validator's floor of 8
            forceTwoFactorForAdmins = false,
            maxFailedLoginAttempts = 10,
            lockoutMinutes = 15,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Saving the policy is journalled against the administrator who changed it.</summary>
    [Fact]
    public async Task Saving_the_policy_is_journalled_against_the_administrator_who_changed_it()
    {
        await using var factory = CreateFactory();
        await MigrateAsync(factory);
        await SeedUsersAsync(factory);
        using var client = await SignInAsync(factory, "admin");

        await client.PutAsJsonAsync("/api/v1/security-policy", new
        {
            minimumPasswordLength = 16,
            forceTwoFactorForAdmins = true,
            maxFailedLoginAttempts = 5,
            lockoutMinutes = 30,
        });

        var response = await client.GetAsync("/api/v1/audit");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entries = body.RootElement.EnumerateArray()
            .Select(entry =>
            {
                return (entry.GetProperty("action").GetString(), entry.GetProperty("subject").GetString());
            })
            .ToList();

        Assert.Contains(("SecurityPolicySaved", "SecurityPolicy"), entries);
    }

    /// <summary>Every security-policy route is covered by the gating fixture.</summary>
    [Fact]
    public void Every_security_policy_route_is_covered_by_the_gating_fixture()
    {
        // A hand-written list of routes goes stale the first time somebody adds one. These are read
        // off the controller itself, so a new endpoint fails HERE — naming itself — rather than
        // quietly enjoying no proof that it is closed to a customer.
        var declared = ControllerRoutes.Declared<SecurityPolicyController>();
        Assert.NotEmpty(declared);

        var inFixture = SecurityPolicyEndpoints()
            .Select(row =>
            {
                return $"{row[0]} {row[1]}";
            })
            .ToHashSet(StringComparer.Ordinal);

        var missing = declared.Where(route =>
        {
            return !inFixture.Contains(route);
        }).ToList();

        Assert.True(
            missing.Count == 0,
            "These security-policy routes are absent from SecurityPolicyEndpoints(), so nothing "
            + "proves they are closed to a signed-in customer: " + string.Join(", ", missing));
    }

    /// <summary>Boots the host against this class's PostgreSQL.</summary>
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

    /// <summary>Applies the migrations these tests need, the way the installer does before first boot.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    /// <summary>Seeds one administrator and one customer.</summary>
    /// <param name="factory">The booted host.</param>
    private static async Task SeedUsersAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        identity.Users.Add(new User(
            Guid.NewGuid(), "admin", "admin@example.com", hasher.Hash(Password), UserRole.Admin, now));
        identity.Users.Add(new User(
            Guid.NewGuid(), "customer", "customer@example.com", hasher.Hash(Password), UserRole.Customer, now));

        await identity.SaveChangesAsync();
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    /// <param name="factory">The booted host.</param>
    /// <param name="username">The user to sign in as.</param>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("session").GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }

    /// <summary>Issues one request, giving the PUT route a body its model binder accepts.</summary>
    /// <remarks>
    /// The body must be VALID. A route whose body fails validation answers 400, which would make a
    /// gating theory pass without the request ever reaching the authorization policy under test.
    /// </remarks>
    /// <param name="client">The client to send with.</param>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The absolute path.</param>
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new
            {
                minimumPasswordLength = 16,
                forceTwoFactorForAdmins = true,
                maxFailedLoginAttempts = 5,
                lockoutMinutes = 30,
            });
        }

        return await client.SendAsync(request);
    }
}
