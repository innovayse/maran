using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Host.Resilience;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the accounts decorator does over a dead agent, measured through the real client and transport.</summary>
public sealed class ResilientAgentAccountsClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline, so a stall fails instead of hanging.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A transport outage on an account deletion is answered as an unavailable agent, not thrown.</summary>
    /// <remarks>
    /// There is no recording fake for this client, so the call goes through the real client and a real
    /// channel to a path with no socket: nothing on the transport path is hand-made.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task A_transport_outage_on_an_account_deletion_answers_agent_unavailable_instead_of_throwing()
    {
        using var agent = UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new ResilientAgentAccountsClient(
            new AgentAccountsClient(channel, NullLogger<AgentAccountsClient>.Instance),
            OperationPipelineRegistry.WithOperationTimeout(30));

        var result = await client.DeleteAsync("alice", default).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }
}
