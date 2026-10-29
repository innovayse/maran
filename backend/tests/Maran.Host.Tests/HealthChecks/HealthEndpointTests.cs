using System.Net;
using System.Text.Json;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.Host.HealthChecks;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>Health endpoint returns ok.</summary>
    [Fact]
    public async Task Health_endpoint_returns_ok()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ok", await response.Content.ReadAsStringAsync());
    }

    /// <summary>Liveness endpoint returns 200 with no dependencies configured at all.</summary>
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

    /// <summary>Readiness endpoint does not require the agent to be reachable.</summary>
    [Fact]
    public async Task Readiness_endpoint_does_not_require_the_agent_to_be_reachable()
    {
        // No agent socket is reachable in this test host, yet the agent alone must never block
        // readiness — only the database decides it.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unavailable", body.RootElement.GetProperty("agent").GetString());
    }

    /// <summary>An agent whose contract revision the window refused is reported incompatible on the summary.</summary>
    [Fact]
    public async Task The_health_summary_reports_an_incompatible_agent()
    {
        var refused = new StubAgentSystemClient(
            Result<AgentInfoDto>.Fail(Error.Of(AgentProtoVersionWindow.IncompatibleCode, ErrorType.Unavailable)));

        var status = await AgentStatusAsync(refused);

        Assert.Equal(AgentHealthProbe.Incompatible, status);
    }

    /// <summary>An agent inside the window is reported connected on the summary (inverse control).</summary>
    [Fact]
    public async Task The_health_summary_reports_a_compatible_agent_as_connected()
    {
        var status = await AgentStatusAsync(new RecordingAgentSystemClient());

        Assert.Equal(AgentHealthProbe.Connected, status);
    }

    /// <summary>Reads the agent word of the <c>/health</c> summary from a host whose agent client is the given stand-in.</summary>
    /// <param name="agent">The client standing in for the agent.</param>
    /// <returns>The agent's reported status.</returns>
    private async Task<string?> AgentStatusAsync(IAgentSystemClient agent)
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(agent);
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("agent").GetString();
    }
}
