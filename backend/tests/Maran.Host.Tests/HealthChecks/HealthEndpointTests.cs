using System.Net;

namespace Maran.Host.Tests.HealthChecks;

/// <summary>Boot-level smoke tests of the host pipeline.</summary>
public sealed class HealthEndpointTests : IClassFixture<PanelTestFactory>
{
    private readonly PanelTestFactory _factory;

    /// <summary>Captures the shared in-memory host factory.</summary>
    public HealthEndpointTests(PanelTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Liveness_endpoint_returns_200_with_no_dependencies_configured_at_all()
    {
        // No connection string and no agent socket are configured for this factory at all —
        // liveness must never depend on either (systemd restarts a process that stops answering
        // this, so a database outage must not turn into a restart loop).
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Readiness_endpoint_does_not_require_the_agent_to_be_reachable()
    {
        // No agent socket is reachable in this test host, yet the agent alone must never block
        // readiness — only the database decides it.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unavailable", body.RootElement.GetProperty("agent").GetString());
    }
}
