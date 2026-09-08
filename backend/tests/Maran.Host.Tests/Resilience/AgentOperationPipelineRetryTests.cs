using System.Net.Sockets;
using Maran.Host.Resilience;

namespace Maran.Host.Tests.Resilience;

/// <summary>
/// The retry POLICY of <see cref="AgentOperationPipeline"/>: how many further chances a transient
/// transport failure gets, and that the pipeline stops rather than retrying for ever.
/// </summary>
/// <remarks>
/// Every other test in this folder sets <c>FailuresBeforeSuccess = 1</c> and asserts two calls.
/// That is deliberately a claim about MEMBERSHIP — that this method goes through the pipeline at
/// all — and it is blind to the number: with one failure planted, a policy of one retry and a
/// policy of five are indistinguishable, so dropping <c>MaxRetryAttempts</c> from two to one left
/// all of them green. The count is one policy fact, and it is pinned here, once, from both ends.
/// Two tests are needed because each is blind to the other's direction: a policy that retried
/// twice and then a third time passes the first assertion, and only the give-up test refuses it.
/// </remarks>
public sealed class AgentOperationPipelineRetryTests
{
    /// <summary>Deadline for any decorated call, so a stalled pipeline fails instead of hanging.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Two transient failures are both retried, so the call succeeds on the third attempt.</summary>
    [Fact]
    public async Task Two_transient_failures_are_both_retried_and_the_third_attempt_succeeds()
    {
        var inner = new RecordingAgentSitesClient { FailuresBeforeSuccess = 2 };

        var result = await NewClient(inner)
            .DeleteAsync("acc1", "example.com", "8.3", default)
            .WaitAsync(TestTimeout);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, inner.Calls);
    }

    /// <summary>A third transient failure is surfaced instead of retried again.</summary>
    [Fact]
    public async Task A_third_transient_failure_is_surfaced_instead_of_retried_again()
    {
        var inner = new RecordingAgentSitesClient { FailuresBeforeSuccess = 3 };

        await Assert.ThrowsAsync<SocketException>(async () =>
        {
            await NewClient(inner)
                .DeleteAsync("acc1", "example.com", "8.3", default)
                .WaitAsync(TestTimeout);
        });

        // Three attempts, not four: the ceiling is what stops a queue of retries piling work on an
        // agent that is already refusing connections.
        Assert.Equal(3, inner.Calls);
    }

    /// <summary>Builds the decorator over a pipeline whose timeout is long enough not to interfere.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorator under test.</returns>
    private static ResilientAgentSitesClient NewClient(RecordingAgentSitesClient inner)
    {
        return new ResilientAgentSitesClient(
            inner,
            OperationPipelineRegistry.WithOperationTimeout(30));
    }
}
