using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Services.SystemService;
using Maran.Agent.Client.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Channels;

/// <summary>The fixed channels give a caller that chose its one channel the same answer whatever the target is.</summary>
public sealed class FixedAgentChannelsTests
{
    /// <summary>Deadline for any wait on a double, so a stalled transport fails instead of hanging.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Every ask returns the very channel it was built with.</summary>
    [Fact]
    public void Every_ask_returns_the_channel_it_was_built_with()
    {
        using var agent = UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var channels = new FixedAgentChannels(channel);

        Assert.Same(channel, channels.ForCurrent());
        Assert.Same(channel, channels.ForCurrent());
        Assert.Same(channel, channels.ForServer(Guid.NewGuid()));
    }

    /// <summary>The client built from a plain channel still sends its calls over that channel.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task A_client_built_from_a_plain_channel_sends_its_calls_over_it()
    {
        using var agent = UnixSocketAgentDouble.AcceptAndHold();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new AgentSystemClient(channel, NullLogger<AgentSystemClient>.Instance);
        using var cancellation = new CancellationTokenSource();

        var pending = client.GetInfoAsync(cancellation.Token);
        await agent.RequestReceived.WaitAsync(TestTimeout);
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
        {
            return pending.WaitAsync(TestTimeout);
        });
    }
}
