using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Host.Configuration;
using Maran.Host.HealthChecks;
using Maran.Host.Resilience;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
        var probe = NewProbe(new StubAgentSystemClient(
            Result<AgentInfoDto>.Ok(new AgentInfoDto(
                "1.0.0",
                "debian",
                "debian",
                AgentProtoVersionWindow.Maximum,
                "/var/backups/maran"))));

        Assert.Equal(AgentHealthProbe.Connected, await probe.ProbeAsync());
    }

    /// <summary>An agent that answers with a failure is reported as unavailable.</summary>
    [Fact]
    public async Task An_agent_that_answers_with_a_failure_is_reported_as_unavailable()
    {
        var probe = NewProbe(new StubAgentSystemClient(Result<AgentInfoDto>.Fail(Error.Of("AgentUnavailable", ErrorType.Unavailable))));

        Assert.Equal(AgentHealthProbe.Unavailable, await probe.ProbeAsync());
    }

    /// <summary>
    /// An agent whose handshake the window refused is reported as incompatible, which is not the same
    /// answer as an agent that did not answer.
    /// </summary>
    [Fact]
    public async Task An_agent_outside_the_window_is_reported_as_incompatible()
    {
        var probe = NewProbe(new StubAgentSystemClient(
            Result<AgentInfoDto>.Fail(Error.Of(AgentProtoVersionWindow.IncompatibleCode, ErrorType.Unavailable))));

        Assert.Equal(AgentHealthProbe.Incompatible, await probe.ProbeAsync());
    }

    /// <summary>An unreachable agent is reported as unavailable, never as incompatible (inverse control).</summary>
    [Fact]
    public async Task An_unreachable_agent_is_reported_unavailable_not_incompatible()
    {
        var probe = NewProbe(new StubAgentSystemClient(
            Result<AgentInfoDto>.Fail(Error.Of("AgentUnavailable", ErrorType.Unavailable))));

        var answer = await probe.ProbeAsync();

        Assert.Equal(AgentHealthProbe.Unavailable, answer);
        Assert.NotEqual(AgentHealthProbe.Incompatible, answer);
    }

    /// <summary>
    /// A socket that does not exist is reported as unavailable through the real client and the real
    /// pipeline, with no catch-all in the probe to hide a transport exception that escaped.
    /// </summary>
    [Fact]
    public async Task A_missing_agent_socket_is_reported_unavailable_without_a_catch_all()
    {
        using var missing = UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(missing.SocketPath);
        var probe = NewProbe(new AgentSystemClient(channel, NullLogger<AgentSystemClient>.Instance));

        var answer = await probe.ProbeAsync().WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(AgentHealthProbe.Unavailable, answer);
    }

    /// <summary>
    /// A defect that is not a transport failure is not swallowed into "unavailable": it leaves the
    /// probe, so a bug is a failed request instead of a health answer that hides it.
    /// </summary>
    [Fact]
    public async Task A_defect_inside_the_client_is_not_reported_as_an_unavailable_agent()
    {
        var probe = NewProbe(new StubAgentSystemClient(new InvalidOperationException("a bug, not a transport failure")));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await probe.ProbeAsync();
        });
    }

    /// <summary>
    /// A real agent that accepts the connection and never answers is reported as unavailable within
    /// the probe's retry ceiling, not after it.
    /// </summary>
    [Fact]
    public async Task A_hung_agent_is_reported_unavailable_within_the_probe_ceiling()
    {
        using var hung = UnixSocketAgentDouble.AcceptAndHold();
        using var channel = AgentChannel.CreateUnixSocket(hung.SocketPath);
        var probe = NewProbe(
            new AgentSystemClient(channel, NullLogger<AgentSystemClient>.Instance),
            probeTimeoutSeconds: 1);
        var started = System.Diagnostics.Stopwatch.StartNew();

        var answer = await probe.ProbeAsync().WaitAsync(TimeSpan.FromSeconds(15));
        started.Stop();

        Assert.Equal(AgentHealthProbe.Unavailable, answer);

        // Three attempts of one second each plus two short backoffs: above one timeout, below ten.
        Assert.InRange(started.Elapsed, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
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
