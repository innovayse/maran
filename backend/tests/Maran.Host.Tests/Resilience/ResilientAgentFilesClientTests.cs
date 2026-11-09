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

    /// <summary>
    /// A transport outage on a streamed write is answered as an unavailable agent, not thrown, although
    /// the upload is deliberately outside the operation pipeline.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task A_transport_outage_on_a_streamed_write_answers_agent_unavailable_instead_of_throwing()
    {
        using var agent = UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new ResilientAgentFilesClient(
            new AgentFilesClient(channel, NullLogger<AgentFilesClient>.Instance),
            OperationPipelineRegistry.WithOperationTimeout(30));

        var result = await client
            .WriteFileStreamAsync("alice", "a.bin", 420, new MemoryStream([1, 2, 3]), 1_024, default)
            .WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }

    /// <summary>A transport outage on a directory listing is answered as an unavailable agent, not thrown.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task A_transport_outage_on_a_listing_answers_agent_unavailable_instead_of_throwing()
    {
        using var agent = UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new ResilientAgentFilesClient(
            new AgentFilesClient(channel, NullLogger<AgentFilesClient>.Instance),
            OperationPipelineRegistry.WithOperationTimeout(30));

        var result = await client
            .ListDirectoryAsync("alice", "", 0, 0, false, DirectorySortOrder.Name, false, default)
            .WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }

    /// <summary>A transport outage on a search is answered as an unavailable agent, not thrown.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task A_transport_outage_on_a_search_answers_agent_unavailable_instead_of_throwing()
    {
        using var agent = UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new ResilientAgentFilesClient(
            new AgentFilesClient(channel, NullLogger<AgentFilesClient>.Instance),
            OperationPipelineRegistry.WithOperationTimeout(30));

        var result = await client
            .SearchEntriesAsync("alice", "", "logo", 0, 0, false, default)
            .WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
    }

    /// <summary>
    /// A transport outage when opening a file for reading is answered as an unavailable agent, not
    /// thrown, although the read is deliberately outside the operation pipeline.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task A_transport_outage_when_opening_a_file_answers_agent_unavailable_instead_of_throwing()
    {
        using var agent = UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new ResilientAgentFilesClient(
            new AgentFilesClient(channel, NullLogger<AgentFilesClient>.Instance),
            OperationPipelineRegistry.WithOperationTimeout(30));

        var result = await client.ReadFileAsync("alice", "a.txt", 1_024, default).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }
}
