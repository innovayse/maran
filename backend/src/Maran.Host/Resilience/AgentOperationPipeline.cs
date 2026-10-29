using Grpc.Core;
using Maran.Agent.Client.Errors;
using Maran.Host.Configuration;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Maran.Host.Resilience;

/// <summary>
/// The pipeline every agent OPERATION goes through: a timeout so a stuck unix-socket call cannot
/// hang the request that made it, a bounded retry on exactly one failure — a connection to the agent
/// that could not be established — and the one translation of what is left into a typed answer
/// (rules/csharp.md "Every outbound call goes through a named resilience pipeline").
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="AgentCallPipeline"/>, which is the health probe's: that one's timeout
/// is a second or two, because a probe's whole purpose is to answer quickly and a slow answer is
/// itself the answer. Creating a system user is not that — <c>useradd</c> writing a home directory
/// on a busy host is legitimately slow, and reusing the probe's timeout here would abandon real
/// work half-done.
/// </para>
/// <para>
/// <b>A TIMED-OUT operation is not retried, and this is the one place that policy is set.</b>
/// An earlier version of this pipeline retried on the timeout as well, on the argument that every
/// agent operation is idempotent by design (spec §9) so a repeat converges. That argument is true
/// of a repeat and false of this case, because the panel's timeout does not stop the agent's work —
/// it only stops watching it. Every operation runs inside <c>tokio::task::spawn_blocking</c>
/// (<c>agent/crates/agent/src/services/wire/run_blocking.rs</c>), and a cancelled call drops the
/// request future, which DETACHES that blocking task rather than cancelling it: the closure runs to
/// completion. So the retry did not repeat an operation, it started a second copy alongside the
/// first — which is the overlapping-call condition the agent's config-write lock exists for
/// (<c>agent/crates/ops/src/safe_write/config_tree_lock.rs</c> documents the three interleavings
/// it closes), manufactured by the panel out of one customer request with no second actor
/// anywhere. Idempotence is a property of REPEATING an operation, never of running two at once,
/// and the two were being conflated here.
/// </para>
/// <para>
/// <b>What IS retried: a call that could not be made.</b> The classification is
/// <see cref="AgentTransportFailure.IsConnectFailure"/>, beside the generated client; the decision to
/// retry on it, and on nothing else, is made here. A connection the kernel refused — a socket path
/// that does not exist, or a socket file the agent left behind when it stopped — delivered no
/// request, so the second attempt is the FIRST attempt at the operation, and the sub-second window
/// of an agent restart is bridged rather than reported. A transport failure raised after the request
/// was sent is not retried, for the timeout's reason: the agent may be doing the work.
/// </para>
/// <para>
/// An earlier version retried on <see cref="System.Net.Sockets.SocketException"/>, and its remarks
/// said why that was safe. The exception never reached this pipeline: <c>Grpc.Net.Client</c> wraps
/// it in an <see cref="RpcException"/> two levels down, Polly matches the thrown type, and the
/// measured attempt count on an outage was one (issue #87). Those remarks also admitted that a socket
/// error raised part-way through a call was indistinguishable here from a connect failure and would
/// be retried anyway; the framework's own connect classification is what makes the two
/// distinguishable now, and the part-way failure is no longer retried.
/// </para>
/// <para>
/// <b>An outage is an answer, not an exception.</b> <see cref="ExecuteAsync{T, TState}"/> is how
/// every agent decorator runs a call through this pipeline, and it turns whatever the transport
/// raised once the pipeline has given up — the refused connection after its last attempt, a failure
/// after the request was sent, the pipeline's own timeout — into a failed <see cref="Result{T}"/>
/// carrying the transport failure's typed code. An unreachable agent is therefore
/// <c>AgentUnavailable</c>, a 503 through the panel's one status mapping, where it used to be the
/// last-resort handler's anonymous 500. The caller's own cancellation is not translated: it leaves
/// as the cancellation it is.
/// </para>
/// <para>
/// The consequence a customer feels: one request waits at the agent for at most
/// <see cref="AgentOptions.OperationTimeout"/> once — the retry is outside the timeout, so each
/// attempt carries its own, but a timed-out attempt is the last one — plus, before it, up to two
/// refused connects, each answered at once, and the two backoffs between them.
/// </para>
/// </remarks>
public static class AgentOperationPipeline
{
    /// <summary>The name this pipeline is registered under.</summary>
    public const string Name = "agent-operation";

    /// <summary>How many times a refused connection is retried before it is surfaced.</summary>
    private const int MaxRetryAttempts = 2;

    /// <summary>The first retry delay; later ones back off exponentially with jitter.</summary>
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>Configures the pipeline on <paramref name="builder"/>.</summary>
    /// <param name="builder">The pipeline being built.</param>
    /// <param name="agentOptions">Supplies <see cref="AgentOptions.OperationTimeout"/>.</param>
    public static void Configure(ResiliencePipelineBuilder builder, AgentOptions agentOptions)
    {
        builder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = MaxRetryAttempts,
                Delay = RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,

                // A call that could not be made, and nothing else. An agent that answered with a
                // typed error answered, and repeating the call would not change its mind; an agent
                // that has not answered YET is still working, and a second call would run beside
                // the first rather than instead of it (see the remarks).
                ShouldHandle = new PredicateBuilder()
                    .Handle<RpcException>(exception =>
                    {
                        return AgentTransportFailure.IsConnectFailure(exception);
                    }),
            })
            .AddTimeout(agentOptions.OperationTimeout);
    }

    /// <summary>
    /// Runs one agent call through <paramref name="pipeline"/> and answers a failure of the transport
    /// as a typed failure instead of letting it escape.
    /// </summary>
    /// <typeparam name="T">The value the call produces.</typeparam>
    /// <typeparam name="TState">What the call needs, passed through so the lambda captures nothing.</typeparam>
    /// <param name="pipeline">This pipeline, as resolved by name from the registry.</param>
    /// <param name="call">The call to make; it receives the state and the pipeline's token.</param>
    /// <param name="state">What <paramref name="call"/> needs.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>
    /// What the call returned; or, when the transport gave up, <see cref="AgentTransportFailure.ToError"/>
    /// of what it raised; or, when this pipeline's timeout did, <see cref="AgentTransportFailure.Unavailable"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Mapped HERE, after the pipeline, and not inside the client the call reaches. The retry decides
    /// on the exception, so a client that turned it into a failed result first would leave the retry
    /// nothing to see — the same blindness, one layer up, that the old predicate had.
    /// </para>
    /// <para>
    /// A cancellation the caller asked for is never an unavailable agent. The channel reports it as
    /// <see cref="OperationCanceledException"/>, which nothing here catches; and an
    /// <see cref="RpcException"/> raised while the caller's token is cancelled is left to propagate
    /// rather than mapped, for a channel built without that option. The timeout is caught by type:
    /// Polly raises <see cref="TimeoutRejectedException"/> only when its own timer fired and the
    /// caller's token had not.
    /// </para>
    /// <para>
    /// No log line is written here. The transport's text names the socket path and the host's socket
    /// error, which is operator material; the pipeline's telemetry, which the registration wires to the
    /// panel's logger, reports each handled attempt and each timeout, and the status detail never
    /// reaches the answer (rules/security.md item 8).
    /// </para>
    /// </remarks>
    public static async Task<Result<T>> ExecuteAsync<T, TState>(
        ResiliencePipeline pipeline,
        Func<TState, CancellationToken, ValueTask<Result<T>>> call,
        TState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pipeline);

        try
        {
            return await pipeline.ExecuteAsync(call, state, cancellationToken);
        }
        catch (RpcException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<T>.Fail(AgentTransportFailure.ToError(exception));
        }
        catch (TimeoutRejectedException)
        {
            return Result<T>.Fail(AgentTransportFailure.Unavailable());
        }
    }
}
