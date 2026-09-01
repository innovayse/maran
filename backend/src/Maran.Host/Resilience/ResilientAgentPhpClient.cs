using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts the agent's PHP version listing through <see cref="AgentOperationPipeline"/>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives.
/// </summary>
/// <remarks>
/// Installing a version is passed straight through. It is a stream that legitimately runs for
/// minutes while a package manager works, so the operation timeout would abandon it half-done, and
/// the retry would restart an install the agent is still performing. The stream states its own
/// ending — including the case where it stopped without one — so nothing is left hanging silently.
/// </remarks>
public sealed class ResilientAgentPhpClient : IAgentPhpClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentPhpClient _inner;

    /// <summary>The named operation pipeline every call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentPhpClient(IAgentPhpClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<PhpVersionDto>>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.ListVersionsAsync(token);
            },
            _inner,
            cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<PhpInstallEvent> InstallVersionAsync(
        string version,
        CancellationToken cancellationToken)
    {
        return _inner.InstallVersionAsync(version, cancellationToken);
    }
}
