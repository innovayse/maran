using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AccountsService;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts every agent account operation through <see cref="AgentOperationPipeline"/>: a timeout, so a
/// stuck unix-socket call cannot hang the HTTP request that made it, and a bounded retry on
/// transport failures (rules/csharp.md "Every outbound call goes through a named resilience
/// pipeline").
/// </summary>
/// <remarks>
/// A decorator rather than a change inside <c>AgentAccountsClient</c>, because the pipeline lives
/// in the Host — the canonical layout puts <c>Resilience/</c> there — while the client project must
/// not depend on the Host. Registered by <see cref="Extensions.ResilienceExtensions"/> over the
/// client the agent project registers, so nothing else has to know the difference.
///
/// Before this existed, the pipeline was registered and resolved by nobody: the whole of
/// <c>ResiliencePipelineProvider</c> appeared in the codebase exactly once, in a doc comment
/// claiming it was used. Account creation, suspension and deletion ran with no timeout at all.
/// </remarks>
public sealed class ResilientAgentAccountsClient : IAgentAccountsClient
{
    private readonly IAgentAccountsClient _inner;
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentAccountsClient(IAgentAccountsClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public Task<Result<CreatedAccountDto>> CreateAsync(
        string username,
        ulong quotaBytes,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(token =>
        {
            return _inner.CreateAsync(username, quotaBytes, token);
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SuspendAsync(string username, CancellationToken cancellationToken)
    {
        return ExecuteAsync(token =>
        {
            return _inner.SuspendAsync(username, token);
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> UnsuspendAsync(string username, CancellationToken cancellationToken)
    {
        return ExecuteAsync(token =>
        {
            return _inner.UnsuspendAsync(username, token);
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<ulong>> DeleteAsync(string username, CancellationToken cancellationToken)
    {
        return ExecuteAsync(token =>
        {
            return _inner.DeleteAsync(username, token);
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetQuotaAsync(string username, ulong quotaBytes, CancellationToken cancellationToken)
    {
        return ExecuteAsync(token =>
        {
            return _inner.SetQuotaAsync(username, quotaBytes, token);
        }, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<AccountUsageDto>> GetUsageAsync(string username, CancellationToken cancellationToken)
    {
        return ExecuteAsync(token =>
        {
            return _inner.GetUsageAsync(username, token);
        }, cancellationToken);
    }

    /// <summary>Runs one call through the pipeline, carrying the caller's cancellation into it.</summary>
    /// <typeparam name="T">The value the call produces.</typeparam>
    /// <param name="call">The call to make.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>What the call returned, once the pipeline has finished with it.</returns>
    private async Task<Result<T>> ExecuteAsync<T>(
        Func<CancellationToken, Task<Result<T>>> call,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state(token);
            },
            call,
            cancellationToken);
    }
}
