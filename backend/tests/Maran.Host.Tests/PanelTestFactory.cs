using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Maran.Host.Tests;

/// <summary>
/// The host factory every test in this project builds on. It boots the real pipeline in a
/// hermetic configuration: no database, no agent, nothing outside the process.
/// </summary>
/// <remarks>
/// The environment is <c>Testing</c> on purpose. Under <c>Development</c> the host would load
/// <c>appsettings.Development.json</c>, whose database points at a developer's local PostgreSQL —
/// so these tests passed on a workstation with the dev container running and failed in CI, where
/// nothing listens on 5432. A test must state its own configuration rather than inherit whatever
/// the machine happens to provide.
///
/// With no database configured, messaging stays in memory and the health probes report the
/// database as not configured — which is exactly the surface these tests assert against. Anything
/// needing a real database is an integration test and lives in Maran.Host.IntegrationTests, where
/// a container is started explicitly.
/// </remarks>
public sealed class PanelTestFactory : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting(PanelTestSettings.EncryptionKeyPath, PanelTestSettings.EncryptionKey);
        builder.UseSetting("Database:Host", string.Empty);
    }
}
