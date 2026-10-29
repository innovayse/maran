using Grpc.Core;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Channels;

/// <summary>
/// What the real channel surfaces for a call that could not complete: the two facts the panel's
/// error mapping stands on, measured against the real transport rather than assumed.
/// </summary>
public sealed class AgentChannelTests
{
    /// <summary>Deadline for any call to a double, so a stalled transport fails instead of hanging.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A call its caller cancelled is a cancellation exception, never a gRPC status.</summary>
    /// <remarks>
    /// Pins <c>ThrowOperationCanceledOnCancellation</c>: without it the caller's cancellation and the
    /// pipeline's timeout both arrive as <c>StatusCode.Cancelled</c>, and the timeout can no longer be
    /// told from a refusal to retry.
    /// </remarks>
    [Fact]
    public async Task A_cancelled_call_surfaces_as_a_cancellation_and_not_a_grpc_status()
    {
        using var agent = UnixSocketAgentDouble.AcceptAndHold();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new Maran.Agent.V1.SystemService.SystemServiceClient(channel);
        using var cancellation = new CancellationTokenSource();
        using var call = client.GetAgentInfoAsync(new GetAgentInfoRequest(), cancellationToken: cancellation.Token);

        await agent.RequestReceived.WaitAsync(TestTimeout);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await call.ResponseAsync.WaitAsync(TestTimeout);
        });
    }

    /// <summary>A path with no socket file arrives as an unavailable status carrying a connection error.</summary>
    [Fact]
    public async Task A_missing_socket_surfaces_as_unavailable_carrying_a_connection_error()
    {
        using var agent = UnixSocketAgentDouble.Missing();

        await AssertConnectionErrorAsync(agent);
    }

    /// <summary>A socket file nobody listens on arrives the same way.</summary>
    [Fact]
    public async Task A_stale_socket_surfaces_as_unavailable_carrying_a_connection_error()
    {
        using var agent = UnixSocketAgentDouble.Stale();

        await AssertConnectionErrorAsync(agent);
    }

    /// <summary>Makes a real call and asserts the status and the framework's connection classification.</summary>
    /// <param name="agent">The double standing in for the agent.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    private static async Task AssertConnectionErrorAsync(UnixSocketAgentDouble agent)
    {
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new Maran.Agent.V1.SystemService.SystemServiceClient(channel);

        var thrown = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.GetAgentInfoAsync(new GetAgentInfoRequest()).ResponseAsync.WaitAsync(TestTimeout);
        });

        Assert.Equal(StatusCode.Unavailable, thrown.StatusCode);
        var inner = Assert.IsType<HttpRequestException>(thrown.InnerException);
        Assert.Equal(HttpRequestError.ConnectionError, inner.HttpRequestError);
    }
}
