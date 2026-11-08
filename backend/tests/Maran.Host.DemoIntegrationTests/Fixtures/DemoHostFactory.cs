using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.DemoIntegrationTests.Fixtures;

/// <summary>The demo host, configured by the test that boots it.</summary>
public sealed class DemoHostFactory : WebApplicationFactory<Maran.Host.Demo.Program>
{
    /// <summary>The test's own configuration of the host.</summary>
    private readonly Action<IWebHostBuilder> _configure;

    /// <summary>Creates a factory that applies <paramref name="configure"/> to the host it boots.</summary>
    /// <param name="configure">Settings and service replacements.</param>
    public DemoHostFactory(Action<IWebHostBuilder> configure)
    {
        _configure = configure;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _configure(builder);
    }
}
