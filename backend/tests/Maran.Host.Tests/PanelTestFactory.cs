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
        builder.UseSetting(PanelTestSettings.JwtSigningKeyPath, PanelTestSettings.JwtSigningKey);
        builder.UseSetting("Database:Host", string.Empty);

        // The agent socket is pinned to a path that cannot exist, for the same reason the
        // environment is pinned to Testing: left at its default, this host addresses
        // /run/maran/agent.sock — the path a real agent listens on. On a workstation where one is
        // running, the probe CONNECTS, and the readiness test asserting that an unreachable agent
        // never blocks readiness fails with "connected" where it expected "unavailable". The test
        // was not wrong about the product; it was inheriting a machine it never configured.
        builder.UseSetting("Agent:SocketPath", "/nonexistent/maran-tests/agent.sock");

        // One second rather than the production default of thirty, because a test that proves the
        // agent-operation pipeline abandons a stuck call has to WAIT for it — three attempts at the
        // default would spend a minute and a half asserting a single timeout. Stated here rather
        // than in the test so the whole composed host runs on one agreed configuration, in the
        // spirit of the remarks above: a test states its own configuration.
        builder.UseSetting("Agent:OperationTimeoutSeconds", "1");
    }
}
