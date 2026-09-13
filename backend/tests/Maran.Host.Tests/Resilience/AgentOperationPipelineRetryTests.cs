using System.Net.Sockets;
using Maran.Host.Resilience;
using Polly.Timeout;

namespace Maran.Host.Tests.Resilience;

/// <summary>
/// The retry POLICY of <see cref="AgentOperationPipeline"/>: how many further chances a call that
/// could not be made gets, that the pipeline stops rather than retrying for ever, and that a call
/// which was made and then timed out gets none at all.
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

    /// <summary>An operation the pipeline timed out on is surfaced, not attempted a second time.</summary>
    /// <remarks>
    /// The other direction of the same policy, and the reason it is a policy rather than a default.
    /// The panel's timeout does not stop the agent's work — every operation runs inside
    /// <c>tokio::task::spawn_blocking</c>, and a cancelled call detaches that task instead of
    /// cancelling it — so a second attempt would run BESIDE the first rather than instead of it,
    /// which is the overlapping-call condition the agent's concurrency audit's findings all need.
    /// One customer request must not produce two concurrent executions.
    ///
    /// The count is the assertion and the exception is not: a policy that retried and then failed
    /// throws the same <see cref="TimeoutRejectedException"/> as one that did not retry at all, so
    /// asserting only the exception would be blind to the very thing this pins.
    /// </remarks>
    [Fact]
    public async Task An_operation_the_pipeline_timed_out_on_is_not_attempted_a_second_time()
    {
        var inner = new RecordingAgentSitesClient { Delay = TimeSpan.FromSeconds(20) };

        await Assert.ThrowsAsync<TimeoutRejectedException>(async () =>
        {
            await NewClient(inner, timeoutSeconds: 1)
                .DeleteAsync("acc1", "example.com", "8.3", default)
                .WaitAsync(TestTimeout);
        });

        Assert.Equal(1, inner.Calls);
    }

    /// <summary>Builds the decorator over a pipeline whose timeout is long enough not to interfere.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <returns>The decorator under test.</returns>
    private static ResilientAgentSitesClient NewClient(RecordingAgentSitesClient inner)
    {
        return NewClient(inner, timeoutSeconds: 30);
    }

    /// <summary>Builds the decorator over a pipeline with a stated per-attempt timeout.</summary>
    /// <param name="inner">The recording client to wrap.</param>
    /// <param name="timeoutSeconds">How long one attempt may take before it is abandoned.</param>
    /// <returns>The decorator under test.</returns>
    private static ResilientAgentSitesClient NewClient(RecordingAgentSitesClient inner, int timeoutSeconds)
    {
        return new ResilientAgentSitesClient(
            inner,
            OperationPipelineRegistry.WithOperationTimeout(timeoutSeconds));
    }
}
