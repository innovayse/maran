using Maran.Agent.Client.Interfaces;
using Maran.Host.Configuration;
using Maran.Host.HealthChecks;
using Maran.Host.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Registry;

namespace Maran.Host.Tests.HealthChecks;

/// <summary>
/// The retry POLICY of <see cref="AgentCallPipeline"/>, the probe pipeline: how many further
/// chances a refused connection gets, and that the probe gives up rather than retrying for ever.
/// </summary>
/// <remarks>
/// <see cref="AgentHealthProbeTests"/> builds this same pipeline but asserts the TIMEOUT, and every
/// retry test in <c>Resilience/</c> plants a single failure, which cannot distinguish one retry
/// from two. So the probe's count had no observer at all. It is asserted here through the probe
/// itself — the only production caller of this pipeline — rather than against a bare delegate, so
/// what is pinned is the behaviour a health endpoint actually gets.
///
/// The ceiling matters as much as the retry, and more here than on the operation pipeline: a probe
/// is meant to answer quickly, and a probe that kept retrying would turn a refused connection into
/// a slow answer, which is the failure the timeout exists to prevent.
/// </remarks>
public sealed class AgentCallPipelineRetryTests
{
    /// <summary>Deadline for a probe, so a runaway retry loop fails instead of hanging.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Two refused connections are both retried, so the third handshake is reported connected.</summary>
    [Fact]
    public async Task Two_refused_connections_are_both_retried_and_the_third_handshake_connects()
    {
        var client = new RecordingAgentSystemClient { FailuresBeforeSuccess = 2 };

        var answer = await NewProbe(client).ProbeAsync().WaitAsync(TestTimeout);

        Assert.Equal(AgentHealthProbe.Connected, answer);
        Assert.Equal(3, client.Calls);
    }

    /// <summary>A third refused connection ends the probe as unavailable instead of being retried again.</summary>
    [Fact]
    public async Task A_third_refused_connection_ends_the_probe_as_unavailable()
    {
        var client = new RecordingAgentSystemClient { FailuresBeforeSuccess = 3 };

        var answer = await NewProbe(client).ProbeAsync().WaitAsync(TestTimeout);

        Assert.Equal(AgentHealthProbe.Unavailable, answer);
        Assert.Equal(3, client.Calls);
    }

    /// <summary>Builds the probe around a client and the real named probe pipeline.</summary>
    /// <param name="client">The client standing in for the agent.</param>
    /// <returns>The probe under test.</returns>
    private static AgentHealthProbe NewProbe(IAgentSystemClient client)
    {
        var services = new ServiceCollection();
        services.AddResiliencePipeline(AgentCallPipeline.Name, builder =>
        {
            AgentCallPipeline.Configure(builder, new AgentOptions { ProbeTimeoutSeconds = 30 });
        });

        var provider = services.BuildServiceProvider();
        return new AgentHealthProbe(client, provider.GetRequiredService<ResiliencePipelineProvider<string>>());
    }
}
