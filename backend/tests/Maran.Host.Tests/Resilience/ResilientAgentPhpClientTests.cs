using Maran.Host.Resilience;
using Maran.Host.Tests.Fixtures;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>What the PHP decorator does: the listing goes through the pipeline, the install does not.</summary>
public sealed class ResilientAgentPhpClientTests
{
    /// <summary>Deadline for any test that waits on the pipeline.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>A transport outage on a listing is answered as an unavailable agent, not thrown.</summary>
    /// <remarks>
    /// The inner fake throws what the real transport throws for a refused connection
    /// (<see cref="AgentTransportFaults.ConnectRefused"/>), so the answer is the decorator's to give: the
    /// pipeline retries the refused connect twice and the shared mapping answers 503's error code.
    /// </remarks>
    [Fact]
    public async Task A_transport_outage_on_a_listing_answers_agent_unavailable_instead_of_throwing()
    {
        var inner = new RecordingAgentPhpClient { FailuresBeforeSuccess = int.MaxValue };

        var result = await NewClient(inner).ListVersionsAsync(default).WaitAsync(TestTimeout);

        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.Equal(3, inner.Calls);
    }

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
