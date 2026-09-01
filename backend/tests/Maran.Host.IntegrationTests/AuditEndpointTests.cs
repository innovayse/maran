using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The audit journal over real HTTP: it records what happened, it is readable by an administrator,
/// and it is readable by nobody else (spec §10, "Аудит").
/// </summary>
/// <remarks>
/// The journal names every actor on the server and the address they connected from, so who may
/// read it is part of its correctness, not a separate concern. It is also the only surface here
/// that is append-only by construction — nothing writes to it over HTTP — so the test that matters
/// most is that a real action performed through the API turns up in it afterwards.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class AuditEndpointTests : IAsyncLifetime
{
    private const string Password = "correct horse battery staple";
    private const string Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AuditEndpointTests(PostgresFixture postgres)
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

    /// <summary>A sign-in through the API turns up in the journal an administrator reads.</summary>
    [Fact]
    public async Task A_sign_in_turns_up_in_the_journal_an_administrator_reads()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory, ("admin", UserRole.Admin));
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/audit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var entries = body.RootElement.EnumerateArray().ToList();
        Assert.Contains(entries, entry =>
        {
            return entry.GetProperty("action").GetString() == "LoginSucceeded";
        });
        Assert.Contains(entries, entry =>
        {
            return entry.GetProperty("actorUsername").GetString() == "admin";
        });
    }

    /// <summary>The journal never carries the password that was typed to produce the entry.</summary>
    [Fact]
    public async Task The_journal_never_carries_the_password_that_produced_the_entry()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory, ("admin", UserRole.Admin));
        using var client = await SignInAsync(factory, "admin");

        var body = await client.GetStringAsync("/api/v1/audit");

        Assert.DoesNotContain(Password, body, StringComparison.Ordinal);
    }

    /// <summary>A signed-in customer cannot read the journal.</summary>
    [Fact]
    public async Task A_signed_in_customer_cannot_read_the_journal()
    {
        // The entries name every actor on the server and the address they came from. A customer
        // reading this page would learn about tenants they have no relationship with at all.
        await using var factory = CreateFactory();
        await SeedAsync(factory, ("admin", UserRole.Admin), ("customer", UserRole.Customer));
        using var client = await SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/audit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An anonymous caller cannot read the journal.</summary>
    [Fact]
    public async Task An_anonymous_caller_cannot_read_the_journal()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory, ("admin", UserRole.Admin));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/audit");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A limit outside the validator's range is refused rather than clamped.</summary>
    [Fact]
    public async Task A_limit_outside_the_allowed_range_is_refused()
    {
        await using var factory = CreateFactory();
        await SeedAsync(factory, ("admin", UserRole.Admin));
        using var client = await SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/audit?limit=100000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
        });
    }

    /// <summary>Migrates, then creates the named users.</summary>
    private static async Task SeedAsync(
        WebApplicationFactory<Program> factory,
        params (string Username, UserRole Role)[] users)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await context.Database.MigrateAsync();

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;
        foreach (var (username, role) in users)
        {
            context.Users.Add(new User(
                Guid.NewGuid(), username, $"{username}@example.com", hasher.Hash(Password), role, now));
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Signs the named user in and returns a client carrying their access token.</summary>
    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/auth/login",
            new { Username = username, Password });

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var accessToken = body.RootElement.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client;
    }
}
