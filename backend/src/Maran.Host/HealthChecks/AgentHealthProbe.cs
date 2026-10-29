using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SystemService;
using Maran.Host.Resilience;
using Polly;
using Polly.Registry;
using Polly.Timeout;

namespace Maran.Host.HealthChecks;

/// <summary>
/// Asks the agent who it is and reports one of three words: <see cref="Connected"/>,
/// <see cref="Unavailable"/> or <see cref="Incompatible"/>. Asked afresh on every call — by
/// <c>/health</c>, <c>/health/ready</c> and <see cref="Maran.Host.BackgroundServices.AgentCompatibilityStartupCheck"/> — and never
/// cached, so an agent that is restarted or upgraded while the panel runs is seen on the next request.
/// </summary>
/// <remarks>
/// <para>
/// <b>Total for every state the agent can be in, without a catch-all.</b> A missing socket, a
/// refused connection and an agent answering outside the contract window all arrive here as a failed
/// <see cref="SharedKernel.Results.Result{T}"/>, because <see cref="AgentSystemClient"/> returns
/// them rather than throwing. A hung agent arrives as the probe pipeline's
/// <see cref="TimeoutRejectedException"/> once its attempts are spent, and is answered
/// <see cref="Unavailable"/> here. A cancellation of the caller's own token propagates, because a
/// caller that left is owed no answer.
/// </para>
/// <para>
/// What is deliberately NOT collapsed into <see cref="Unavailable"/> is a defect in the panel itself:
/// any other exception escapes, so the health request fails loudly and the log names it. This probe
/// used to catch every exception and report it as an absent agent, which is the blindness
/// <see cref="DatabaseHealthProbe"/> documents from the other side — a broken probe reported as a
/// broken dependency, with nothing in the log. The catch-all was needed only while a transport
/// failure reached this code as an exception (issue #87), and it no longer does.
/// </para>
/// </remarks>
public sealed class AgentHealthProbe
{
    /// <summary>
    /// Reported when the handshake succeeds within the timeout and the revision is inside the window.
    /// </summary>
    public const string Connected = "connected";

    /// <summary>
    /// Reported when the agent could not be reached, did not answer in time, or answered the handshake
    /// with a failure instead of its identity — an absent agent, a restarting one, a refused peer
    /// credential.
    /// </summary>
    public const string Unavailable = "unavailable";

    /// <summary>
    /// Reported when the agent answered, in a contract revision outside the window this panel drives
    /// (<see cref="AgentProtoVersionWindow"/>): an upgrade replaced one side and not the other. The only
    /// answer that fails readiness on the agent's account, because traffic sent now would be served by a
    /// panel driving an agent whose answers it may misread.
    /// </summary>
    public const string Incompatible = "incompatible";

    /// <summary>The client used to reach the agent's system service.</summary>
    private readonly IAgentSystemClient _agentClient;

    /// <summary>The named probe pipeline, which carries the configured timeout.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Creates the probe around the agent client and its pipeline.</summary>
    /// <param name="agentClient">Client used to perform the handshake.</param>
    /// <param name="pipelines">The registry the named probe pipeline is resolved from.</param>
    /// <remarks>
    /// The timeout comes from <see cref="AgentCallPipeline"/>, not from a constant here. It was a
    /// hard-coded two seconds, which made <c>Agent:ProbeTimeoutSeconds</c> configuration that
    /// nothing read: the pipeline honouring it was registered and resolved by nobody, and this
    /// probe ignored it. Both halves of that gap are now one.
    /// </remarks>
    public AgentHealthProbe(IAgentSystemClient agentClient, ResiliencePipelineProvider<string> pipelines)
    {
        _agentClient = agentClient;
        _pipeline = pipelines.GetPipeline(AgentCallPipeline.Name);
    }

    /// <summary>Performs the handshake and reports what the agent is to this panel.</summary>
    /// <param name="cancellationToken">
    /// The caller's own cancellation — the request that asked, or the host stopping. Not a timeout:
    /// the probe pipeline carries that.
    /// </param>
    /// <returns>
    /// <see cref="Connected"/> for a handshake inside the window, <see cref="Incompatible"/> for one
    /// outside it, otherwise <see cref="Unavailable"/>.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<string> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _pipeline.ExecuteAsync(
                async token =>
                {
                    return await _agentClient.GetInfoAsync(token);
                },
                cancellationToken);

            if (result.IsSuccess)
            {
                return Connected;
            }

            return result.Error?.Code == AgentProtoVersionWindow.IncompatibleCode ? Incompatible : Unavailable;
        }
        catch (TimeoutRejectedException)
        {
            // The agent accepted the connection and never answered, on every attempt the pipeline
            // made. A health answer reports that; it does not wait longer for it.
            return Unavailable;
        }
    }
}
