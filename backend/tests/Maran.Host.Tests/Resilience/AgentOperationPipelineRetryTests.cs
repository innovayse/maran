using Grpc.Core;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Client.Tests.TestSupport;
using Maran.Host.Resilience;
using Maran.Host.Tests.Fixtures;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

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

    /// <summary>A third transient failure is answered as an unavailable agent instead of retried again.</summary>
    [Fact]
    public async Task A_third_transient_failure_is_surfaced_instead_of_retried_again()
    {
        var inner = new RecordingAgentSitesClient { FailuresBeforeSuccess = 3 };

        var result = await NewClient(inner)
            .DeleteAsync("acc1", "example.com", "8.3", default)
            .WaitAsync(TestTimeout);

        AssertUnavailable(result);

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
    /// The count is the assertion and the answer is not: a policy that retried and then failed
    /// answers the same <c>AgentUnavailable</c> as one that did not retry at all, so asserting only
    /// the answer would be blind to the very thing this pins.
    /// </remarks>
    [Fact]
    public async Task An_operation_the_pipeline_timed_out_on_is_not_attempted_a_second_time()
    {
        var inner = new RecordingAgentSitesClient { Delay = TimeSpan.FromSeconds(20) };

        var result = await NewClient(inner, timeoutSeconds: 1)
            .DeleteAsync("acc1", "example.com", "8.3", default)
            .WaitAsync(TestTimeout);

        AssertUnavailable(result);
        Assert.Equal(1, inner.Calls);
    }

    /// <summary>
    /// A refused connection, as the REAL transport raises it, is retried twice and then answered as an
    /// unavailable agent.
    /// </summary>
    /// <remarks>
    /// The original defect: the retry matched an exception type the transport never throws, so against
    /// a real dead socket the attempt count was one. No fake sits on the transport path here — the
    /// calls go through a real channel to a path with no listener.
    /// </remarks>
    /// <param name="stale">Whether the socket file exists and nobody listens, rather than being absent.</param>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_refused_connection_is_retried_twice_and_answered_unavailable(bool stale)
    {
        using var agent = stale ? UnixSocketAgentDouble.Stale() : UnixSocketAgentDouble.Missing();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new AgentSitesClient(channel, NullLogger<AgentSitesClient>.Instance);
        var calls = 0;

        var result = await AgentOperationPipeline.ExecuteAsync(
            Pipeline(timeoutSeconds: 30),
            async (sites, token) =>
            {
                calls++;
                return await sites.DeleteAsync("acc1", "example.com", "8.3", token);
            },
            client,
            CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(3, calls);
        AssertUnavailable(result);
    }

    /// <summary>A failure raised after the request was sent is answered, and attempted only once.</summary>
    /// <remarks>
    /// The agent may be doing the work, so a second attempt would run beside the first.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task An_unavailable_raised_after_the_request_was_sent_is_not_attempted_a_second_time()
    {
        var calls = 0;

        var result = await AgentOperationPipeline.ExecuteAsync<bool, int>(
            Pipeline(timeoutSeconds: 30),
            (_, _) =>
            {
                calls++;
                throw AgentTransportFaults.AfterRequestSent();
            },
            0,
            CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(1, calls);
        AssertUnavailable(result);
    }

    /// <summary>An operation against an agent that accepts and never answers is abandoned once.</summary>
    /// <remarks>
    /// Over the real channel: the pipeline's timeout cancels the call, the channel reports that as a
    /// cancellation, the timeout strategy turns it into a timeout, and nothing retries it.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task An_operation_against_a_hung_agent_is_abandoned_once_and_answered_unavailable()
    {
        using var agent = UnixSocketAgentDouble.AcceptAndHold();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new AgentSitesClient(channel, NullLogger<AgentSitesClient>.Instance);
        var calls = 0;

        var result = await AgentOperationPipeline.ExecuteAsync(
            Pipeline(timeoutSeconds: 1),
            async (sites, token) =>
            {
                calls++;
                return await sites.DeleteAsync("acc1", "example.com", "8.3", token);
            },
            client,
            CancellationToken.None).WaitAsync(TestTimeout);

        Assert.Equal(1, calls);
        AssertUnavailable(result);
    }

    /// <summary>A call the caller cancelled is a cancellation, not an unavailable agent.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task A_call_the_caller_cancelled_is_not_reported_as_an_unavailable_agent()
    {
        using var agent = UnixSocketAgentDouble.AcceptAndHold();
        using var channel = AgentChannel.CreateUnixSocket(agent.SocketPath);
        var client = new AgentSitesClient(channel, NullLogger<AgentSitesClient>.Instance);
        using var cancellation = new CancellationTokenSource();

        var running = AgentOperationPipeline.ExecuteAsync(
            Pipeline(timeoutSeconds: 30),
            async (sites, token) =>
            {
                return await sites.DeleteAsync("acc1", "example.com", "8.3", token);
            },
            client,
            cancellation.Token);

        await agent.RequestReceived.WaitAsync(TestTimeout);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await running.WaitAsync(TestTimeout);
        });
    }

    /// <summary>A cancelled status raised while the caller's token is cancelled is rethrown, not mapped.</summary>
    /// <remarks>
    /// The real channel cannot produce this shape (it throws a cancellation exception instead), so a
    /// lambda that cancels the token and then raises the status is the only way to reach the guard.
    /// </remarks>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task A_cancelled_rpc_status_raised_while_the_callers_token_is_cancelled_is_rethrown_not_mapped()
    {
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await AgentOperationPipeline.ExecuteAsync<bool, int>(
                Pipeline(timeoutSeconds: 30),
                (_, _) =>
                {
                    cancellation.Cancel();
                    throw new RpcException(new Status(StatusCode.Cancelled, "Call canceled"));
                },
                0,
                cancellation.Token).WaitAsync(TestTimeout);
        });
    }

    /// <summary>Asserts the answer is the unavailable-agent failure, of the kind that is a 503.</summary>
    /// <typeparam name="T">The value the call would have produced.</typeparam>
    /// <param name="result">The answer under test.</param>
    private static void AssertUnavailable<T>(Result<T> result)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }

    /// <summary>Resolves the real operation pipeline with a stated per-attempt timeout.</summary>
    /// <param name="timeoutSeconds">How long one attempt may take before it is abandoned.</param>
    /// <returns>The pipeline production builds.</returns>
    private static Polly.ResiliencePipeline Pipeline(int timeoutSeconds)
    {
        return OperationPipelineRegistry.WithOperationTimeout(timeoutSeconds).GetPipeline(AgentOperationPipeline.Name);
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
