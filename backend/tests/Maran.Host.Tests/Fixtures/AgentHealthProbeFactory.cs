using Maran.Agent.Client.Interfaces;
using Maran.Host.Configuration;
using Maran.Host.HealthChecks;
using Maran.Host.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Registry;

namespace Maran.Host.Tests.Fixtures;

/// <summary>Builds the agent health probe over a given client and the real named probe pipeline.</summary>
/// <remarks>
/// <see cref="AgentHealthProbe"/> is sealed, so a test that needs one around a stand-in client builds the
/// real probe over the real <see cref="AgentCallPipeline"/> instead of substituting the probe.
/// </remarks>
internal static class AgentHealthProbeFactory
{
    /// <summary>Builds the probe.</summary>
    /// <param name="client">The client standing in for the agent.</param>
    /// <param name="probeTimeoutSeconds">The configured probe timeout, in seconds.</param>
    /// <returns>The probe.</returns>
    public static AgentHealthProbe Create(IAgentSystemClient client, int probeTimeoutSeconds = 30)
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline(AgentCallPipeline.Name, builder =>
        {
            AgentCallPipeline.Configure(builder, new AgentOptions { ProbeTimeoutSeconds = probeTimeoutSeconds });
        });

        var provider = services.BuildServiceProvider();
        return new AgentHealthProbe(client, provider.GetRequiredService<ResiliencePipelineProvider<string>>());
    }
}
