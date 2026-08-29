using Maran.Host.Configuration;
using Polly.Retry;
using Polly.Timeout;
using Polly;

namespace Maran.Host.Resilience;

/// <summary>
/// The resilience pipeline every call to the agent over gRPC goes through: a hard timeout so a
/// stuck unix-socket call cannot hang the caller, plus a small number of retries on transient
/// failures only (rules/csharp.md "Every outbound call goes through a named resilience
/// pipeline"). Registered under <see cref="Name"/> and resolved via
/// <c>ResiliencePipelineProvider&lt;string&gt;</c> wherever <c>IAgentSystemClient</c> is used —
/// currently just the health probe (<c>HealthEndpoint</c>).
/// </summary>
public static class AgentCallPipeline
{
    /// <summary>The name this pipeline is registered under.</summary>
    public const string Name = "agent";

    /// <summary>Retries attempted after the first failed call, before giving up.</summary>
    private const int MaxRetryAttempts = 2;

    /// <summary>Base delay between retries; grows exponentially with jitter.</summary>
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Adds the timeout and retry strategies to <paramref name="builder"/>. Timeout comes from
    /// <see cref="AgentOptions.ProbeTimeout"/> so operators tune one value for both the probe
    /// deadline and the pipeline's own ceiling. Retries only fire on <see cref="TimeoutRejectedException"/>
    /// and <see cref="System.Net.Sockets.SocketException"/> — a connection refused or a slow agent,
    /// never a well-formed <c>Result.Fail</c> the agent returned deliberately, since that is not a
    /// transient failure a retry could fix.
    /// </summary>
    /// <param name="builder">The pipeline builder to configure.</param>
    /// <param name="agentOptions">Supplies the timeout the pipeline enforces per attempt.</param>
    public static void Configure(ResiliencePipelineBuilder builder, AgentOptions agentOptions)
    {
        builder
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = MaxRetryAttempts,
                Delay = RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder()
                    .Handle<TimeoutRejectedException>()
                    .Handle<System.Net.Sockets.SocketException>(),
            })
            .AddTimeout(agentOptions.ProbeTimeout);
    }
}
