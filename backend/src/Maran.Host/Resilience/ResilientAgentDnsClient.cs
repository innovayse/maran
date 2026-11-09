using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DnsService;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts every agent DNS operation through <see cref="AgentOperationPipeline"/>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives: without the decorator the call has no timeout at all,
/// and a stuck unix socket hangs the HTTP request that made it.
/// </summary>
/// <remarks>
/// The pipeline retries exactly one thing, a connection to the agent's socket that could not be
/// established, which delivered no request. Every member below is therefore safe to dial again: the
/// second attempt is the first attempt at the operation. A timeout is not retried, because the panel's
/// timeout stops its watching of the agent's work and not the work itself. Every method is decorated,
/// the deleting one included, because a method that reaches the inner client directly runs with no
/// timeout while every test stays green.
/// </remarks>
public sealed class ResilientAgentDnsClient : IAgentDnsClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentDnsClient _inner;

    /// <summary>The named operation pipeline every call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentDnsClient(IAgentDnsClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<Result<DnsDaemonStatusDto>> EnableAsync(CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.EnableAsync(token);
            },
            _inner,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<DnsDaemonStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.DisableAsync(token);
            },
            _inner,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<DnsDaemonStatusDto>> GetStatusAsync(CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.GetStatusAsync(token);
            },
            _inner,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<DnsZoneServedDto>> PutZoneAsync(
        DnsZoneDefinition zone,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.PutZoneAsync(state.Zone, token);
            },
            (Client: _inner, Zone: zone),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.DeleteZoneAsync(state.ZoneName, token);
            },
            (Client: _inner, ZoneName: zoneName),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<DnsZoneServedDto>> GetZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.GetZoneAsync(state.ZoneName, token);
            },
            (Client: _inner, ZoneName: zoneName),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<string>>> ListZonesAsync(CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.ListZonesAsync(token);
            },
            _inner,
            cancellationToken);
    }
}
