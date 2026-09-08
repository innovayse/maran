using Maran.Agent.Client.Services.BackupService;
using Maran.Host.Resilience;
using Maran.SharedKernel.Security;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the backup decorator does: the unary calls go through the pipeline, the streams do not.</summary>
public sealed class ResilientAgentBackupClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Listing retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Listing_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentBackupClient { FailuresBeforeSuccess = 1 };

        var result = await NewClient(inner).ListAsync("alice", Local(), default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>Deleting retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Deleting_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentBackupClient { FailuresBeforeSuccess = 1 };

        var result = await NewClient(inner).DeleteAsync("alice", "b1", Local(), default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>Probing retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Probing_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentBackupClient { FailuresBeforeSuccess = 1 };

        var result = await NewClient(inner).ProbeAsync(Local(), default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>A failed restore stream is never retried.</summary>
    /// <remarks>
    /// The reason the streams are passed through rather than wrapped, and the one that is not merely
    /// about duration: a half-streamed restore replayed from the start is a SECOND restore, dropping
    /// the databases the first attempt had already put back. One entry into the inner client is the
    /// whole assertion.
    /// </remarks>
    [Fact]
    public async Task A_failed_restore_stream_is_never_retried()
    {
        var inner = new RecordingAgentBackupClient { StreamsFail = true };
        var client = NewClient(inner);

        async Task DrainAsync()
        {
            await foreach (var unused in client.RestoreAsync("alice", "b1", Local(), "deadbeef", [], default))
            {
                // The events themselves are the client's contract, not the decorator's.
            }
        }

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await DrainAsync().WaitAsync(TestTimeout);
        });

        Assert.Equal(1, inner.StreamStarts);
    }

    /// <summary>A failed create stream is never retried.</summary>
    [Fact]
    public async Task A_failed_create_stream_is_never_retried()
    {
        var inner = new RecordingAgentBackupClient { StreamsFail = true };
        var client = NewClient(inner);

        async Task DrainAsync()
        {
            await foreach (var unused in client.CreateAsync("alice", "b1", Local(), default))
            {
                // The events themselves are the client's contract, not the decorator's.
            }
        }

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await DrainAsync().WaitAsync(TestTimeout);
        });

        Assert.Equal(1, inner.StreamStarts);
    }

    /// <summary>The create stream forwards the backup id it was asked for.</summary>
    /// <remarks>
    /// The inverse control for the two non-retry tests: a decorator that had stopped calling the
    /// inner client at all would satisfy "entered once" trivially by entering it never.
    /// </remarks>
    [Fact]
    public async Task The_create_stream_forwards_the_backup_id_it_was_asked_for()
    {
        var inner = new RecordingAgentBackupClient();
        var client = NewClient(inner);

        async Task DrainAsync()
        {
            await foreach (var unused in client.CreateAsync("alice", "b7", Local(), default))
            {
                // The events themselves are the client's contract, not the decorator's.
            }
        }

        await DrainAsync().WaitAsync(TestTimeout);

        Assert.Equal("b7", inner.LastBackupId);
        Assert.Equal(1, inner.StreamStarts);
    }

    /// <summary>The agent's own root-only backup directory.</summary>
    /// <returns>A local destination.</returns>
    private static AgentBackupDestination Local()
    {
        return new AgentBackupDestination(
            AgentBackupDestinationKind.Local,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new SensitiveString(string.Empty),
            new SensitiveString(string.Empty),
            false);
    }

    /// <summary>Builds the decorator over a pipeline whose timeout is long enough not to interfere.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorator under test.</returns>
    private static ResilientAgentBackupClient NewClient(RecordingAgentBackupClient inner)
    {
        return new ResilientAgentBackupClient(inner, OperationPipelineRegistry.WithOperationTimeout(30));
    }
}
