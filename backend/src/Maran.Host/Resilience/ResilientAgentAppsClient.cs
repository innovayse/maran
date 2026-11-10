using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AppsService;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts the agent's two app listings through <see cref="AgentOperationPipeline"/>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives.
/// </summary>
/// <remarks>
/// Installing, removing and controlling an app are passed straight through. An install and a removal are
/// streams that legitimately run for minutes while a package manager works, so the operation timeout
/// would abandon one half-done and the retry would restart work the agent is still performing. A service
/// control is a unary call, but a retried restart is a second restart of a web server that takes every
/// site off the air, and the caller is told the outcome of the one it asked for. The streams state their
/// own ending, including the case where they stopped without one.
/// </remarks>
public sealed class ResilientAgentAppsClient : IAgentAppsClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentAppsClient _inner;

    /// <summary>The named operation pipeline the listings below are executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentAppsClient(IAgentAppsClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentApp>>> ListAppsAsync(CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.ListAppsAsync(token);
            },
            _inner,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentInstallableApp>>> ListInstallableAppsAsync(
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.ListInstallableAppsAsync(token);
            },
            _inner,
            cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<AppOperationEvent> InstallAppAsync(string appId, CancellationToken cancellationToken)
    {
        return _inner.InstallAppAsync(appId, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<AppOperationEvent> UninstallAppAsync(string appId, CancellationToken cancellationToken)
    {
        return _inner.UninstallAppAsync(appId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentAppServiceStatus>> ControlServiceAsync(
        string appId,
        AgentAppServiceAction action,
        CancellationToken cancellationToken)
    {
        return await _inner.ControlServiceAsync(appId, action, cancellationToken);
    }
}
