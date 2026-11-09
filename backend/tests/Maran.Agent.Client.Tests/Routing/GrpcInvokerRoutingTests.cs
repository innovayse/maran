using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Services.BackupService;
using Maran.Agent.Client.Services.SystemService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Agent.V1;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Routing;

/// <summary>
/// An invoker holds routed channels and not a channel: the channel a call travels over is the one
/// selected when the call starts, so one invoker serves every server the panel manages.
/// </summary>
public sealed class GrpcInvokerRoutingTests
{
    /// <summary>Deadline for any wait on a double, so a stalled transport fails instead of hanging.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Two calls through one invoker reach two different agents when the selection changes between them.</summary>
    /// <remarks>
    /// The inverse control is the assertion that the second agent has seen nothing before the switch:
    /// an invoker that took its channel at construction would send both calls to the first agent, and
    /// the second wait would time out.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task Consecutive_calls_reach_the_agent_selected_when_each_call_starts()
    {
        using var first = UnixSocketAgentDouble.AcceptAndHold();
        using var second = UnixSocketAgentDouble.AcceptAndHold();
        using var firstChannel = AgentChannel.CreateUnixSocket(first.SocketPath);
        using var secondChannel = AgentChannel.CreateUnixSocket(second.SocketPath);
        var channels = new SwitchableAgentChannels(firstChannel);
        var invoker = new GrpcSystemServiceInvoker(channels);
        using var cancellation = new CancellationTokenSource();

        var firstCall = invoker.GetAgentInfoAsync(cancellation.Token);
        await first.RequestReceived.WaitAsync(TestTimeout);
        Assert.False(second.RequestReceived.IsCompleted);

        channels.Current = secondChannel;
        var secondCall = invoker.GetAgentInfoAsync(cancellation.Token);
        await second.RequestReceived.WaitAsync(TestTimeout);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        {
            return firstCall.WaitAsync(TestTimeout);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        {
            return secondCall.WaitAsync(TestTimeout);
        });
        Assert.Equal(2, channels.Requests);
    }

    /// <summary>A streamed call asks for its channel when enumeration starts, once, and not when the invoker is built.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task A_streamed_call_reads_its_channel_once_when_enumeration_starts()
    {
        using var first = UnixSocketAgentDouble.AcceptAndHold();
        using var second = UnixSocketAgentDouble.AcceptAndHold();
        using var firstChannel = AgentChannel.CreateUnixSocket(first.SocketPath);
        using var secondChannel = AgentChannel.CreateUnixSocket(second.SocketPath);
        var channels = new SwitchableAgentChannels(firstChannel);
        var invoker = new GrpcBackupServiceInvoker(channels);
        using var cancellation = new CancellationTokenSource();

        var stream = invoker.CreateBackupAsync(new CreateBackupRequest(), cancellation.Token);
        Assert.Equal(0, channels.Requests);

        channels.Current = secondChannel;
        await using var enumerator = stream.GetAsyncEnumerator(cancellation.Token);
        var moving = enumerator.MoveNextAsync().AsTask();
        await second.RequestReceived.WaitAsync(TestTimeout);

        Assert.False(first.RequestReceived.IsCompleted);
        Assert.Equal(1, channels.Requests);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        {
            return moving.WaitAsync(TestTimeout);
        });
    }

    /// <summary>The client over routed channels follows the selection: an outage is a result, a held agent receives the call.</summary>
    /// <remarks>
    /// The same client, first aimed at a path with no socket (the unavailable answer is a result, not
    /// an exception) and then at an agent that accepts and holds (it receives the request). Together
    /// they show the client's calls follow the selection in both directions.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task The_client_follows_the_selection_between_an_outage_and_a_live_agent()
    {
        using var held = UnixSocketAgentDouble.AcceptAndHold();
        using var missing = UnixSocketAgentDouble.Missing();
        using var heldChannel = AgentChannel.CreateUnixSocket(held.SocketPath);
        using var missingChannel = AgentChannel.CreateUnixSocket(missing.SocketPath);
        var channels = new SwitchableAgentChannels(missingChannel);
        var client = new AgentSystemClient(channels, NullLogger<AgentSystemClient>.Instance);

        var outage = await client.GetInfoAsync(CancellationToken.None).WaitAsync(TestTimeout);

        Assert.False(outage.IsSuccess);
        Assert.Equal("AgentUnavailable", outage.Error!.Code);
        Assert.False(held.RequestReceived.IsCompleted);

        channels.Current = heldChannel;
        using var cancellation = new CancellationTokenSource();
        var pending = client.GetInfoAsync(cancellation.Token);
        await held.RequestReceived.WaitAsync(TestTimeout);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        {
            return pending.WaitAsync(TestTimeout);
        });
        Assert.Equal(2, channels.Requests);
    }
}
