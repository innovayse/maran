using System.Net;
using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Ssl.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// The ACME authority read (<c>GET /api/v1/certificates/acme-directory</c>) over real HTTP: an
/// administrator is answered with the directory the host is configured with, a customer is refused, and
/// an anonymous caller is not let in.
/// </summary>
[Collection(SharedDatabase.Name)]
public sealed class AcmeDirectoryEndpointTests : IAsyncLifetime
{
    /// <summary>The route under test.</summary>
    private const string Route = "/api/v1/certificates/acme-directory";

    /// <summary>The directory URL the host is booted with.</summary>
    private const string ConfiguredUrl = "https://ca.internal.example:14000/dir";

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public AcmeDirectoryEndpointTests(PostgresFixture postgres)
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

    /// <summary>An administrator is answered with the configured directory, its host and its kind.</summary>
    [Fact]
    public async Task An_administrator_is_answered_with_the_configured_directory()
    {
        await using var factory = await BootAsync();
        using var admin = await EndpointHarness.SignInAsync(factory, "admin");

        var response = await admin.GetAsync(Route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ConfiguredUrl, body.RootElement.GetProperty("url").GetString());
        Assert.Equal("ca.internal.example", body.RootElement.GetProperty("host").GetString());
        Assert.Equal("custom", body.RootElement.GetProperty("kind").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("kindName").GetString()));
    }

    /// <summary>A customer is refused with forbidden, because the authority is the operator's configuration.</summary>
    [Fact]
    public async Task A_customer_is_refused()
    {
        await using var factory = await BootAsync();
        var (_, accountId) = await EndpointHarness.SeedAccountAsync(factory, "olive");
        await EndpointHarness.SeedCustomerAsync(factory, "customer", accountId);
        using var customer = await EndpointHarness.SignInAsync(factory, "customer");

        var response = await customer.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>An anonymous caller is answered unauthorized.</summary>
    [Fact]
    public async Task An_anonymous_caller_is_answered_unauthorized()
    {
        await using var factory = await BootAsync();
        using var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>Boots the host with the configured directory, migrated and with an administrator.</summary>
    /// <returns>The booted factory, which the caller disposes.</returns>
    private async Task<WebApplicationFactory<Program>> BootAsync()
    {
        var factory = EndpointHarness.CreateFactory(
            _pg,
            services =>
            {
                services.Configure<AcmeOptions>(options => { options.DirectoryUrl = ConfiguredUrl; });
            });
        await EndpointHarness.MigrateAsync(factory);
        await EndpointHarness.SeedAdministratorAsync(factory);

        return factory;
    }
}
