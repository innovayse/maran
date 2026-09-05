using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.Host.Configuration;
using Maran.Host.HealthChecks;
using Maran.Host.Resilience;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Registry;

namespace Maran.Host.Tests.HealthChecks;

/// <summary>
/// Behavioural contract of <see cref="AgentHealthProbe"/>: it answers, always, and quickly.
/// </summary>
/// <remarks>
/// The probe had no test at all while it carried a hard-coded two-second timeout that made
/// <c>Agent:ProbeTimeoutSeconds</c> configuration nothing read. The property worth asserting is not
/// which number is used but that a hung agent produces an answer rather than a hung request — a
/// health endpoint that blocks takes the panel out of a load balancer for the wrong reason.
/// </remarks>
public sealed class AgentHealthProbeTests
{
    /// <summary>A reachable agent is reported as connected.</summary>
    [Fact]
    public async Task A_reachable_agent_is_reported_as_connected()
    {
        var probe = NewProbe(new StubAgentSystemClient(Result<AgentInfoDto>.Ok(new AgentInfoDto("1.0.0", "debian", "debian", 1))));

        Assert.Equal(AgentHealthProbe.Connected, await probe.ProbeAsync());
    }

    /// <summary>An agent that answers with a failure is reported as unavailable.</summary>
    [Fact]
    public async Task An_agent_that_answers_with_a_failure_is_reported_as_unavailable()
    {
        var probe = NewProbe(new StubAgentSystemClient(Result<AgentInfoDto>.Fail(Error.Of("AgentUnavailable", ErrorType.Unavailable))));

        Assert.Equal(AgentHealthProbe.Unavailable, await probe.ProbeAsync());
    }

    /// <summary>A transport exception is reported as unavailable rather than thrown.</summary>
    [Fact]
    public async Task A_transport_exception_is_reported_as_unavailable_rather_than_thrown()
    {
        var probe = NewProbe(new StubAgentSystemClient(new InvalidOperationException("socket is gone")));

        Assert.Equal(AgentHealthProbe.Unavailable, await probe.ProbeAsync());
    }

    /// <summary>A hung agent is reported as unavailable instead of hanging the caller.</summary>
    [Fact]
    public async Task A_hung_agent_is_reported_as_unavailable_instead_of_hanging_the_caller()
    {
        // The client never returns on its own. Without the pipeline's timeout this test does not
        // fail, it hangs — which is exactly what the health endpoint did before it had one.
        var probe = NewProbe(StubAgentSystemClient.ThatNeverAnswers(), probeTimeoutSeconds: 1);

        var answer = await probe.ProbeAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(AgentHealthProbe.Unavailable, answer);
    }

    /// <summary>Builds the probe around a stub client and the real named pipeline.</summary>
    /// <param name="client">The stub standing in for the agent.</param>
    /// <param name="probeTimeoutSeconds">The configured probe timeout.</param>
    /// <returns>The probe under test.</returns>
    private static AgentHealthProbe NewProbe(IAgentSystemClient client, int probeTimeoutSeconds = 2)
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
