using System.Text.Json;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Modules.Licensing.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.IntegrationTests;

/// <summary>
/// Proves the §229 property this slice exists to hold at the Host boundary: the panel starts and
/// serves <c>/health</c> normally no matter what the installed licence says — absent (the default;
/// already covered by <see cref="HostBootTests"/>), and here, actively bad.
/// </summary>
/// <remarks>
/// This slice ships no persistence for a stored licence (out of scope — see
/// <c>ILicenceRawTextSource</c>'s own remarks), so there is no real "install a bad licence file" step
/// available yet. The seam that interface exists FOR is exercised instead:
/// <c>ConfigureTestServices</c> below replaces the registered
/// <c>ILicenceRawTextSource</c> with one that hands the verifier text that fails every stage
/// (unparsable JSON, and separately, a well-formed but forged envelope), the same shape a real
/// storage-backed implementation could one day read off disk. What this test cannot observe: a real
/// licence file actually read from disk, an EF-persisted row, or the fingerprint check §228 names
/// (see the module's own log entry for why).
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class LicensingStartupTests : IAsyncLifetime
{
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public LicensingStartupTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>A raw-text source that always answers with unparsable text — the <c>Malformed</c> case.</summary>
    private sealed class MalformedLicenceTextSource : ILicenceRawTextSource
    {
        public string? ReadRawLicenceText()
        {
            return "this is not a licence envelope";
        }
    }

    /// <summary>The panel boots and serves <c>/health</c> with a licence that fails to parse at all.</summary>
    [Fact]
    public async Task Host_boots_and_serves_health_with_a_malformed_licence()
    {
        await using var factory = BuildFactory(services =>
        {
            services.AddSingleton<ILicenceRawTextSource, MalformedLicenceTextSource>();
        });

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var agent = body.RootElement.GetProperty("agent").GetString();
        Assert.True(agent is "connected" or "unavailable");
    }

    /// <summary>Builds the same boot configuration <see cref="HostBootTests"/> uses, with one override.</summary>
    /// <param name="configureServices">Applied after the panel's own registrations, so it wins.</param>
    /// <returns>The configured, not-yet-started test factory.</returns>
    private WebApplicationFactory<Program> BuildFactory(Action<IServiceCollection> configureServices)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            // Same settings HostBootTests uses — see its own comments for why each is required.
            b.UseEnvironment("Testing");
            foreach (var setting in DatabaseSettings.From(_pg.GetConnectionString()))
            {
                b.UseSetting(setting.Key, setting.Value);
            }

            b.UseSetting("Security:EncryptionKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");
            b.UseSetting("Jwt:SigningKey", "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=");

            foreach (var setting in FirewallSettings.Required())
            {
                b.UseSetting(setting.Key, setting.Value);
            }

            b.ConfigureTestServices(configureServices);
        });
    }
}
