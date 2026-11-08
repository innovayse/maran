using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Host.Resilience;
using Maran.Host.Tests.Fixtures;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Host.Tests.Resilience;

/// <summary>
/// The resilience decorators add a policy and no routing of their own: whichever server the caller
/// selected is the one every attempt of the call reaches, and concurrent calls keep their own.
/// </summary>
public sealed class ResilientAgentClientRoutingTests
{
    /// <summary>Deadline for any decorated call, so a stalled pipeline fails instead of hanging.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>All three attempts of a refused call go to the server selected before the call.</summary>
    /// <remarks>
    /// The pipeline retries a refused connection twice. If the selection were read once and lost, or
    /// read from a place the retry does not flow through, a later attempt would throw for want of a
    /// selection or land on another server; the recorded selections show neither happened.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task Every_attempt_of_a_retried_call_reaches_the_server_selected_before_the_call()
    {
        using var missing = UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(missing.SocketPath);
        var channels = new AmbientSelectionAgentChannels(new() { ["node"] = channel });
        var client = NewClient(channels);

        AmbientSelectionAgentChannels.Select("node");
        var result = await client.DeleteAsync("acc1", "example.com", "8.3", default).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(["node", "node", "node"], channels.Seen);
    }

    /// <summary>Two calls running at once, aimed at different servers, each reach their own.</summary>
    /// <remarks>
    /// The inverse control: a selection held in a field instead of the async context would make one of
    /// the two calls see the other's name, and the per-call counts would not be three and three.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task Concurrent_calls_selected_for_different_servers_each_reach_their_own()
    {
        using var first = UnixSocketAgentDouble.Missing();
        using var second = UnixSocketAgentDouble.Missing();
        using var firstChannel = AgentChannel.CreateUnixSocket(first.SocketPath);
        using var secondChannel = AgentChannel.CreateUnixSocket(second.SocketPath);
        var channels = new AmbientSelectionAgentChannels(new() { ["a"] = firstChannel, ["b"] = secondChannel });
        var client = NewClient(channels);

        await Task.WhenAll(CallAsync(client, "a"), CallAsync(client, "b")).WaitAsync(TestTimeout);

        Assert.Equal(3, channels.Seen.Count(name =>
        {
            return name == "a";
        }));
        Assert.Equal(3, channels.Seen.Count(name =>
        {
            return name == "b";
        }));
    }

    /// <summary>A refused server is answered with the refusal's failure and is attempted once, never retried.</summary>
    /// <remarks>
    /// The pipeline retries a refused CONNECTION. A refusal is made before any connection is tried and
    /// stays true until an administrator changes the server, so a second attempt could only repeat it;
    /// the single request recorded is what separates the two.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task A_refused_server_is_answered_with_its_refusal_and_attempted_once()
    {
        var channels = new RefusingAgentChannels(Error.Of("AgentServerDisabled", ErrorType.Unavailable));
        var client = new ResilientAgentSitesClient(
            new AgentSitesClient(channels, NullLogger<AgentSitesClient>.Instance),
            OperationPipelineRegistry.WithOperationTimeout(30));

        var result = await client.DeleteAsync("acc1", "example.com", "8.3", default).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentServerDisabled", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.Equal(1, channels.Requests);
    }

    /// <summary>Selects <paramref name="name"/> in a context of its own and makes one decorated call.</summary>
    /// <param name="client">The decorated client.</param>
    /// <param name="name">The server to aim the call at.</param>
    /// <returns>A task that completes when the call has returned.</returns>
    private static Task CallAsync(ResilientAgentSitesClient client, string name)
    {
        return Task.Run(async () =>
        {
            AmbientSelectionAgentChannels.Select(name);
            await client.DeleteAsync("acc1", "example.com", "8.3", default);
        });
    }

    /// <summary>Builds the real decorator over the real client and the real operation pipeline.</summary>
    /// <param name="channels">The routed channels the inner client sends over.</param>
    /// <returns>The decorated client.</returns>
    private static ResilientAgentSitesClient NewClient(AmbientSelectionAgentChannels channels)
    {
        return new ResilientAgentSitesClient(
            new AgentSitesClient(channels, NullLogger<AgentSitesClient>.Instance),
            OperationPipelineRegistry.WithOperationTimeout(30));
    }
}
