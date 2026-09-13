using Maran.Host.Configuration;
using Polly;
using Polly.Retry;

namespace Maran.Host.Resilience;

/// <summary>
/// The pipeline every agent OPERATION goes through: a timeout so a stuck unix-socket call cannot
/// hang the request that made it, plus a bounded retry on transient transport failures
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
/// <b>A TIMED-OUT operation is not retried, and this is the one place that distinction is made.</b>
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
/// anywhere. Idempotence is a property of REPEATING an
/// operation, never of running two at once, and the two were being conflated here.
/// </para>
/// <para>
/// A <see cref="System.Net.Sockets.SocketException"/> is still retried, because it is the failure of
/// a call that could not be made: a refused connection to the unix socket started no work for a
/// second attempt to collide with. <b>Stated limit:</b> a socket error raised part-way through a
/// call that HAD reached the agent is indistinguishable here from a connect failure, so on that
/// path the retry can still overlap. It is left retried deliberately — the agent's own per-resource
/// locks are what refuse the overlap, and the panel's job is to surface that refusal rather than to
/// hold a second lock of its own (see the report named above).
/// </para>
/// <para>
/// The consequence a customer feels: the bound on how long one request can wait at the agent is now
/// exactly <see cref="AgentOptions.OperationTimeout"/>, one attempt, instead of three attempts plus
/// two backoffs.
/// </para>
/// </remarks>
public static class AgentOperationPipeline
{
    /// <summary>The name this pipeline is registered under.</summary>
    public const string Name = "agent-operation";

    /// <summary>How many times a transient failure is retried before it is surfaced.</summary>
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
                    .Handle<System.Net.Sockets.SocketException>(),
            })
            .AddTimeout(agentOptions.OperationTimeout);
    }
}
