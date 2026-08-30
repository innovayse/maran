using Maran.Host.Configuration;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Maran.Host.Resilience;

/// <summary>
/// The pipeline every agent OPERATION goes through: a timeout so a stuck unix-socket call cannot
/// hang the request that made it, plus a bounded retry on transient transport failures
/// (rules/csharp.md "Every outbound call goes through a named resilience pipeline").
/// </summary>
/// <remarks>
/// Separate from <see cref="AgentCallPipeline"/>, which is the health probe's: that one's timeout
/// is a second or two, because a probe's whole purpose is to answer quickly and a slow answer is
/// itself the answer. Creating a system user is not that — <c>useradd</c> writing a home directory
/// on a busy host is legitimately slow, and reusing the probe's timeout here would abandon real
/// work half-done.
///
/// Retrying a mutating call is safe here for one specific reason, and only that reason: every
/// agent operation is idempotent by design (spec §9), so a retry after a timeout converges rather
/// than creating a second account or deleting twice. If an operation is ever added that is not
/// idempotent, it must not use this pipeline.
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

                // Transport failures only. An agent that answered with a typed error answered, and
                // repeating the call would not change its mind.
                ShouldHandle = new PredicateBuilder()
                    .Handle<TimeoutRejectedException>()
                    .Handle<System.Net.Sockets.SocketException>(),
            })
            .AddTimeout(agentOptions.OperationTimeout);
    }
}
