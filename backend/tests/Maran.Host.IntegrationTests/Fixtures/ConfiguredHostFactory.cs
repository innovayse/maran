using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// The panel host, configured by the test that boots it. It is the factory itself, not a derived
/// factory created from a throwaway root one: <c>new WebApplicationFactory&lt;Program&gt;().WithWebHostBuilder(...)</c>
/// leaves the root factory undisposed, and the caller can only dispose the derived one it was handed.
/// </summary>
public sealed class ConfiguredHostFactory : WebApplicationFactory<Program>
{
    /// <summary>The test's own configuration of the host.</summary>
    private readonly Action<IWebHostBuilder> _configure;

    /// <summary>Creates a factory that applies <paramref name="configure"/> to the host it boots.</summary>
    /// <param name="configure">The test's settings, environment and service replacements.</param>
    public ConfiguredHostFactory(Action<IWebHostBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _configure = configure;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _configure(builder);
    }
}
