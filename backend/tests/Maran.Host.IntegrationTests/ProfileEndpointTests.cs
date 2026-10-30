using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// <c>GET /api/v1/profile</c> over real HTTP: every signed-in person — administrator and customer
/// alike — reads their own profile, and an anonymous caller reads nothing.
/// </summary>
/// <remarks>
/// The administrator case is the reason the endpoint exists: the customer's hosting-account
/// endpoint answers 404 for anybody who owns no account, so the profile must not depend on one.
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ProfileEndpointTests : IAsyncLifetime
{
    /// <summary>This test's own database on the assembly's shared PostgreSQL server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ProfileEndpointTests(PostgresFixture postgres)
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

    /// <summary>An administrator, who owns no hosting account, reads their own profile.</summary>
    [Fact]
    public async Task An_administrator_reads_their_own_profile()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await client.GetAsync("/api/v1/profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("admin", body.RootElement.GetProperty("username").GetString());
        Assert.Equal("admin@example.com", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("admin", body.RootElement.GetProperty("role").GetString());
        Assert.False(body.RootElement.GetProperty("isTwoFactorEnabled").GetBoolean());
        Assert.Equal(1, body.RootElement.GetProperty("activeSessionCount").GetInt32());
    }

    /// <summary>A customer reads their own profile, not the administrator's.</summary>
    [Fact]
    public async Task A_customer_reads_their_own_profile()
    {
        await using var factory = await BootAsync();
        using var client = await EndpointHarness.SignInAsync(factory, "customer");

        var response = await client.GetAsync("/api/v1/profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("customer", body.RootElement.GetProperty("username").GetString());
        Assert.Equal("customer", body.RootElement.GetProperty("role").GetString());
    }

    /// <summary>An anonymous caller is refused.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_refused()
    {
        await using var factory = await BootAsync();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Boots the host, migrated, with one administrator and one customer.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(_pg);
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "acme");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", accountId);

        return factory;
    }
}
