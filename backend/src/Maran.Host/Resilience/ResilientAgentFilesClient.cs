using Maran.Agent.Client.Interfaces;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts every agent customer-file operation through <see cref="AgentOperationPipeline"/>, for the
/// reason <see cref="ResilientAgentAccountsClient"/> gives: without the decorator the call has no
/// timeout at all, and a stuck unix socket hangs the request that made it.
/// </summary>
public sealed class ResilientAgentFilesClient : IAgentFilesClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentFilesClient _inner;

    /// <summary>The named operation pipeline every call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentFilesClient(IAgentFilesClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> WriteFileAsync(
        string accountUsername,
        string path,
        string content,
        uint mode,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.WriteFileAsync(
                    state.AccountUsername, state.Path, state.Content, state.Mode, token);
            },
            (Client: _inner, AccountUsername: accountUsername, Path: path, Content: content, Mode: mode),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteEntryAsync(
        string accountUsername,
        string path,
        bool recursive,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.DeleteEntryAsync(
                    state.AccountUsername, state.Path, state.Recursive, token);
            },
            (Client: _inner, AccountUsername: accountUsername, Path: path, Recursive: recursive),
            cancellationToken);
    }
}
