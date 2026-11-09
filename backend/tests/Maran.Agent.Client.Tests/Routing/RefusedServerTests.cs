using Maran.Agent.Client.Services.BackupService;
using Maran.Agent.Client.Services.FilesService;
using Maran.Agent.Client.Services.SystemService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Agent.Client.Tests.Routing;

/// <summary>
/// A server the panel declined to dial reaches the caller as the refusal's own typed failure, from
/// every shape of client call: a unary call, a streamed upload, a streamed read and an event stream.
/// </summary>
public sealed class RefusedServerTests
{
    /// <summary>The refusal every test raises: a server that exists but is not approved yet.</summary>
    private static readonly Error Refusal = Error.Of("AgentServerNotApproved", ErrorType.Unavailable);

    /// <summary>The handshake against a refused server is a failed result carrying the refusal.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task The_handshake_against_a_refused_server_is_a_failed_result_with_the_refusals_code()
    {
        var channels = new RefusingAgentChannels(Refusal);
        var client = new AgentSystemClient(channels, NullLogger<AgentSystemClient>.Instance);

        var result = await client.GetInfoAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentServerNotApproved", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.Equal(1, channels.Requests);
    }

    /// <summary>A streamed upload to a refused server is a failed result, not an exception.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task A_streamed_upload_to_a_refused_server_is_a_failed_result()
    {
        var client = new AgentFilesClient(
            new RefusingAgentChannels(Refusal), NullLogger<AgentFilesClient>.Instance);

        var result = await client.WriteFileStreamAsync(
            "alice", "a.bin", 420, new MemoryStream([1, 2, 3]), 1_024, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentServerNotApproved", result.Error!.Code);
    }

    /// <summary>Opening a read on a refused server is a failed result, not an exception.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task Opening_a_read_on_a_refused_server_is_a_failed_result()
    {
        var client = new AgentFilesClient(
            new RefusingAgentChannels(Refusal), NullLogger<AgentFilesClient>.Instance);

        var result = await client.ReadFileAsync("alice", "a.txt", 1_024, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentServerNotApproved", result.Error!.Code);
    }

    /// <summary>An event stream aimed at a refused server ends in one failed event with the refusal's code.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task An_event_stream_aimed_at_a_refused_server_ends_in_one_failed_event()
    {
        var channels = new RefusingAgentChannels(Refusal);
        var client = new AgentBackupClient(channels, NullLogger<AgentBackupClient>.Instance);
        var events = new List<BackupCreateEvent>();

        await foreach (var item in client.CreateAsync(
            "alice", "b1", new AgentLocalBackupDestination(), CancellationToken.None))
        {
            events.Add(item);
        }

        var terminal = Assert.Single(events);
        Assert.Equal(BackupCreateEventKind.Failed, terminal.Kind);
        Assert.Equal("AgentServerNotApproved", terminal.ErrorCode);
        Assert.Equal(1, channels.Requests);
    }
}
