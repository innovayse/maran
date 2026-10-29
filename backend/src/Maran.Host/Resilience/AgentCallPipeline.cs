using Grpc.Core;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Services.SystemService;
using Maran.Host.Configuration;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Maran.Host.Resilience;

/// <summary>
/// The resilience pipeline the agent HANDSHAKE goes through: a hard timeout so a stuck unix-socket
/// call cannot hang the caller, plus a small number of retries when the agent could not be reached
/// or did not answer in time (rules/csharp.md "Every outbound call goes through a named resilience
/// pipeline"). Registered under <see cref="Name"/> and resolved via
/// <c>ResiliencePipelineProvider&lt;string&gt;</c>. This is the PROBE pipeline: its timeout is a
/// second or two, which is right for a question whose slow answer is itself an answer, and wrong
/// for work. Operations use <see cref="AgentOperationPipeline"/> instead.
/// </summary>
/// <remarks>
/// It may retry more than the operation pipeline does, and that is the one difference between their
/// predicates. <c>GetAgentInfo</c> changes nothing on the server, so a second attempt after a request
/// that DID reach the agent overlaps nothing — the reason the operation pipeline refuses to retry a
/// timeout or a failure after the request was sent does not exist here.
/// </remarks>
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
    /// deadline and the pipeline's own ceiling.
    /// </summary>
    /// <param name="builder">The pipeline builder to configure.</param>
    /// <param name="agentOptions">Supplies the timeout the pipeline enforces per attempt.</param>
    /// <remarks>
    /// <para>
    /// Retries fire on three outcomes, each the shape one kind of "the agent did not answer" really
    /// takes on this channel. An attempt this pipeline's timeout abandoned is a
    /// <see cref="TimeoutRejectedException"/>, which Polly produces because the channel reports the
    /// cancellation as <see cref="OperationCanceledException"/>
    /// (<see cref="Maran.Agent.Client.Channels.AgentChannel"/>). An agent the transport could not
    /// reach, before or after the request was sent, is an <see cref="RpcException"/> that
    /// <see cref="AgentTransportFailure.IsUnreachable"/> recognises. And a handshake whose client
    /// already turned that exception into its answer is a failed <see cref="Result{T}"/> of
    /// <see cref="AgentInfoDto"/> carrying exactly <see cref="AgentTransportFailure.Unavailable"/> —
    /// matched as a result because, once the client has mapped it, there is no exception left for an
    /// exception predicate to see.
    /// </para>
    /// <para>
    /// Never retried: a well-formed failure the agent returned deliberately, and a handshake refused
    /// for any other reason, since neither is a transient failure a retry could fix. An earlier
    /// version named <see cref="System.Net.Sockets.SocketException"/> as the refused connection; the
    /// transport never throws it (issue #87), and its timeout arm never fired either, because the
    /// channel then reported a timeout as a cancelled gRPC status rather than as a cancellation.
    /// </para>
    /// </remarks>
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
                    .Handle<RpcException>(exception =>
                    {
                        return AgentTransportFailure.IsUnreachable(exception);
                    })
                    .HandleResult(outcome =>
                    {
                        return outcome is Result<AgentInfoDto> { IsSuccess: false } failure
                            && failure.Error == AgentTransportFailure.Unavailable();
                    }),
            })
            .AddTimeout(agentOptions.ProbeTimeout);
    }
}
