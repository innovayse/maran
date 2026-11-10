using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.TransferService;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts the agent's unary source calls through <see cref="AgentOperationPipeline"/>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives.
/// </summary>
/// <remarks>
/// <para>
/// The three unary calls (generating a key, scanning host keys, testing a source) read or mint and change
/// nothing on any host, so repeating one is as safe as repeating a listing. The pipeline retries ONLY a
/// connection to the agent that could not be established, never a call that started, and answers an agent
/// that cannot be reached at all as <c>AgentUnavailable</c>. A call whose retry would be harmful does not
/// exist here: a source that answered and refused (key refused, host key changed) is an answer, not a
/// failed connect, and is never repeated.
/// </para>
/// <para>
/// The artifact export and import are passed straight through as well. The export is a stream that states its
/// own ending (a transport that broke ends it in <see cref="AgentBackupExportFailed"/>) and replaying one that
/// had delivered chunks would hand the caller the same bytes twice. The import consumes a one-shot source of
/// chunks, so a retry could not resend what was already read; a refusal or a broken transport is an answer the
/// caller acts on, never a connect to repeat.
///
/// The inventory read is passed straight through. It is a stream that states its own ending, including the
/// ending where the agent stopped without one and the ending where the transport broke
/// (<see cref="AgentSourceInventoryFailed"/>), so nothing is left hanging silently; and replaying a stream
/// that had already delivered accounts would hand the caller the same accounts twice.
/// </para>
/// </remarks>
public sealed class ResilientAgentTransferClient : IAgentTransferClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentTransferClient _inner;

    /// <summary>The named operation pipeline every unary call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentTransferClient(IAgentTransferClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<AgentSourceResult<AgentGeneratedSourceKey>> GenerateSourceKeyAsync(
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            async (client, token) =>
            {
                return await client.GenerateSourceKeyAsync(token);
            },
            _inner,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<AgentSourceResult<IReadOnlyList<AgentScannedHostKey>>> ScanHostKeyAsync(
        AgentSourceEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.ScanHostKeyAsync(state.Endpoint, token);
            },
            (Client: _inner, Endpoint: endpoint),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<AgentSourceResult<AgentTestedSource>> TestSourceAsync(
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.TestSourceAsync(state.Endpoint, state.Credential, token);
            },
            (Client: _inner, Endpoint: endpoint, Credential: credential),
            cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<AgentSourceInventoryEvent> ReadSourceInventoryAsync(
        AgentSourceEndpoint endpoint,
        AgentSourceCredential credential,
        CancellationToken cancellationToken)
    {
        return _inner.ReadSourceInventoryAsync(endpoint, credential, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<AgentFilePullEvent> PullAccountFilesAsync(
        AgentFilePullRequest request,
        CancellationToken cancellationToken)
    {
        return _inner.PullAccountFilesAsync(request, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<AgentDatabaseTransferEvent> StreamRemoteDatabaseAsync(
        AgentDatabaseTransferRequest request,
        CancellationToken cancellationToken)
    {
        return _inner.StreamRemoteDatabaseAsync(request, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<AgentBackupExportEvent> ExportBackupAsync(
        string accountUsername,
        string backupId,
        Maran.SharedKernel.Security.SensitiveString signingKey,
        CancellationToken cancellationToken)
    {
        return _inner.ExportBackupAsync(accountUsername, backupId, signingKey, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<AgentBackupImportResult> ImportBackupAsync(
        AgentBackupImportHeader header,
        IAsyncEnumerable<ReadOnlyMemory<byte>> chunks,
        CancellationToken cancellationToken)
    {
        return _inner.ImportBackupAsync(header, chunks, cancellationToken);
    }

    /// <summary>
    /// Runs one unary call through the pipeline and answers a failure of the transport as a typed failure.
    /// </summary>
    /// <typeparam name="T">The value the call produces.</typeparam>
    /// <typeparam name="TState">What the call needs, passed through so the lambda captures nothing.</typeparam>
    /// <param name="call">The call to make; it receives the state and the pipeline's token.</param>
    /// <param name="state">What <paramref name="call"/> needs.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>
    /// What the call returned, reason included; or, when the transport gave up or the pipeline's timeout
    /// fired, the typed failure <see cref="AgentOperationPipeline"/> maps it to, with no reason.
    /// </returns>
    /// <remarks>
    /// The pipeline's own mapping answers in <see cref="Result{T}"/>, which has no room for a reason, so the
    /// source result travels through it as a value and is unwrapped here. A call that completed is never
    /// altered: its failure and its reason pass through untouched.
    /// </remarks>
    private async Task<AgentSourceResult<T>> ExecuteAsync<T, TState>(
        Func<TState, CancellationToken, Task<AgentSourceResult<T>>> call,
        TState state,
        CancellationToken cancellationToken)
    {
        var outcome = await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (callState, token) =>
            {
                return Result<AgentSourceResult<T>>.Ok(await call(callState, token));
            },
            state,
            cancellationToken);

        return outcome.IsSuccess ? outcome.Value : AgentSourceResult.Fail<T>(outcome.Error!);
    }
}
