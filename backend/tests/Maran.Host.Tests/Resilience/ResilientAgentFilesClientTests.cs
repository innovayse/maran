using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Services.FilesService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Host.Resilience;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the files decorator does over a dead agent, measured through the real client and transport.</summary>
public sealed class ResilientAgentFilesClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline, so a stall fails instead of hanging.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A transport outage on a file write is answered as an unavailable agent, not thrown.</summary>
    /// <remarks>
    /// The call goes through the real client and a real channel to a path with no socket, so nothing
    /// on the transport path is hand-made.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task A_transport_outage_on_a_file_write_answers_agent_unavailable_instead_of_throwing()
    {
        using var agent = UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new ResilientAgentFilesClient(
            new AgentFilesClient(channel, NullLogger<AgentFilesClient>.Instance),
            OperationPipelineRegistry.WithOperationTimeout(30));

        var result = await client.WriteFileAsync("alice", "index.html", "x", 420, default).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }
}
