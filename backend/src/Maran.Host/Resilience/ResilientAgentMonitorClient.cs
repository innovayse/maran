using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MonitorService;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts every agent monitoring read through <see cref="AgentOperationPipeline"/>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives: without the decorator the call has no timeout
/// at all, and a stuck unix socket hangs the HTTP request that made it.
/// </summary>
/// <remarks>
/// Every method here is read-only, and every one of them is still decorated. A monitoring call is
/// the easiest kind to wave through as harmless, and it is the one most likely to be made from a
/// dashboard that polls: a metrics read against a wedged host would otherwise hold a request open
/// for as long as the host stayed wedged, once per poll, per viewer.
/// </remarks>
public sealed class ResilientAgentMonitorClient : IAgentMonitorClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentMonitorClient _inner;

    /// <summary>The named operation pipeline every call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentMonitorClient(IAgentMonitorClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentHostMetrics>> GetHostMetricsAsync(CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.GetHostMetricsAsync(token);
            },
            _inner,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentServiceStatus>>> GetServiceStatusesAsync(
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.GetServiceStatusesAsync(token);
            },
            _inner,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentAccountDiskUsage>>> GetAccountsDiskUsageAsync(
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.GetAccountsDiskUsageAsync(token);
            },
            _inner,
            cancellationToken);
    }
}
