using Maran.Host.Resilience;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the PHP decorator does: the listing goes through the pipeline, the install does not.</summary>
public sealed class ResilientAgentPhpClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Listing retries a transport failure through the pipeline.</summary>
    [Fact]
    public async Task Listing_retries_a_transport_failure_through_the_pipeline()
    {
        var inner = new RecordingAgentPhpClient { FailuresBeforeSuccess = 1 };
        var client = NewClient(inner);

        var result = await client.ListVersionsAsync(default).WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, inner.Calls);
    }

    /// <summary>The install forwards the version it was asked for.</summary>
    /// <remarks>
    /// The install stream is passed through rather than wrapped: a package manager legitimately runs
    /// for minutes, so the operation timeout would abandon it and the retry would restart an install
    /// the agent is still performing.
    /// </remarks>
    [Fact]
    public async Task The_install_forwards_the_version_it_was_asked_for()
    {
        var inner = new RecordingAgentPhpClient();
        var client = NewClient(inner);

        async Task DrainAsync()
        {
            await foreach (var unused in client.InstallVersionAsync("8.4", default))
            {
                // The events themselves are the client's contract, not the decorator's.
            }
        }

        await DrainAsync().WaitAsync(TestTimeout);

        Assert.Equal("8.4", inner.LastVersion);
    }

    /// <summary>Builds the decorator over a pipeline whose timeout is long enough not to interfere.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorator under test.</returns>
    private static ResilientAgentPhpClient NewClient(RecordingAgentPhpClient inner)
    {
        return new ResilientAgentPhpClient(
            inner,
            OperationPipelineRegistry.WithOperationTimeout(30));
    }
}
