using Grpc.Core;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Host.Configuration;
using Maran.Host.HealthChecks;
using Maran.Host.Resilience;
using Maran.Host.Tests.Fixtures;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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

    /// <summary>A third refusal ends the probe as unavailable instead of being retried again.</summary>
    [Fact]
    public async Task A_third_refused_connection_ends_the_probe_as_unavailable()
    {
        var client = new RecordingAgentSystemClient { FailuresBeforeSuccess = 3 };

        var answer = await NewProbe(client).ProbeAsync().WaitAsync(TestTimeout);

        Assert.Equal(AgentHealthProbe.Unavailable, answer);
        Assert.Equal(3, client.Calls);
    }

    /// <summary>
    /// Against a socket that does not exist, the REAL handshake client answers unavailable on every
    /// attempt, and the probe makes exactly three of them (the first plus two retries) before it gives up.
    /// </summary>
    [Fact]
    public async Task A_missing_agent_socket_is_retried_twice_and_the_probe_gives_up_after_three()
    {
        using var missing = UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(missing.SocketPath);
        var client = new CountingAgentSystemClient(
            new AgentSystemClient(channel, NullLogger<AgentSystemClient>.Instance));

        var answer = await NewProbe(client).ProbeAsync().WaitAsync(TestTimeout);

        Assert.Equal(AgentHealthProbe.Unavailable, answer);
        Assert.Equal(3, client.Calls);
    }

    /// <summary>A handshake refused as incompatible is final: retrying it cannot change the agent's revision.</summary>
    [Fact]
    public async Task A_handshake_refused_as_incompatible_is_not_retried()
    {
        var client = new CountingAgentSystemClient(new StubAgentSystemClient(
            Result<AgentInfoDto>.Fail(Error.Of(AgentProtoVersionWindow.IncompatibleCode, ErrorType.Unavailable))));

        var answer = await NewProbe(client).ProbeAsync().WaitAsync(TestTimeout);

        Assert.Equal(AgentHealthProbe.Incompatible, answer);
        Assert.Equal(1, client.Calls);
    }

    /// <summary>
    /// A handshake that THROWS an unreachable-agent <see cref="RpcException"/> is retried twice, like a
    /// failed result is, and the exception escapes once the attempts are spent.
    /// </summary>
    /// <remarks>
    /// The real client never lets this exception out of the handshake, so this arm is a defence for a
    /// client that does, and the only way to observe it is a client that throws. The fault is
    /// <see cref="AgentTransportFaults.AfterRequestSent"/>, not a refused connect: it is unreachable
    /// but NOT a connect failure, so a predicate narrowed to <c>IsConnectFailure</c> fails this test
    /// too, and a predicate replaced by <c>false</c> makes one call instead of three. The exception is
    /// not translated into <see cref="AgentHealthProbe.Unavailable"/>: the probe catches only the
    /// pipeline's own timeout, so a defect-shaped exception stays loud (the probe's contract).
    /// </remarks>
    [Fact]
    public async Task A_thrown_unreachable_agent_exception_is_retried_twice_and_then_escapes()
    {
        var client = new CountingAgentSystemClient(new StubAgentSystemClient(AgentTransportFaults.AfterRequestSent()));

        var thrown = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await NewProbe(client).ProbeAsync().WaitAsync(TestTimeout);
        });

        Assert.Equal(StatusCode.Unavailable, thrown.StatusCode);
        Assert.Equal(3, client.Calls);
    }

    /// <summary>A thrown <see cref="RpcException"/> that does not mean an unreachable agent is not retried.</summary>
    /// <remarks>
    /// The inverse control of the test above: a predicate mutated to retry every <see cref="RpcException"/>
    /// passes that test and dies here, which keeps the predicate from being read as "retry anything thrown".
    /// </remarks>
    [Fact]
    public async Task A_thrown_rpc_exception_that_is_not_an_unreachable_agent_is_not_retried()
    {
        var client = new CountingAgentSystemClient(new StubAgentSystemClient(
            new RpcException(new Status(StatusCode.Internal, "the server failed the call"))));

        await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await NewProbe(client).ProbeAsync().WaitAsync(TestTimeout);
        });

        Assert.Equal(1, client.Calls);
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
