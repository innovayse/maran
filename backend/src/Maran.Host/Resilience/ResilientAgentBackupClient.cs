using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.BackupService;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts the agent's unary backup calls through <see cref="AgentOperationPipeline"/>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives.
/// </summary>
/// <remarks>
/// Creating and restoring are passed straight through, and not merely because they are slow. A
/// create or a restore legitimately runs for minutes while an archive is written or a database is
/// loaded, so the operation timeout would abandon one half-done — and the retry is worse than
/// useless here: a half-streamed restore replayed from the start is a SECOND restore, dropping the
/// databases the first attempt had already put back. Both streams state their own ending, including
/// the ending where they stopped without one, so nothing is left hanging silently.
///
/// The probe is a network call the agent makes on the panel's behalf and is retried like any other
/// unary call; retrying it cannot change anything on the server, because it only reads.
/// </remarks>
public sealed class ResilientAgentBackupClient : IAgentBackupClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentBackupClient _inner;

    /// <summary>The named operation pipeline every unary call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentBackupClient(IAgentBackupClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<BackupCreateEvent> CreateAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        return _inner.CreateAsync(accountUsername, backupId, destination, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<BackupRestoreEvent> RestoreAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        string expectedSha256,
        IReadOnlyList<string> allowedDatabases,
        CancellationToken cancellationToken)
    {
        return _inner.RestoreAsync(
            accountUsername,
            backupId,
            destination,
            expectedSha256,
            allowedDatabases,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentBackupSummary>>> ListAsync(
        string accountUsername,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.ListAsync(state.AccountUsername, state.Destination, token);
            },
            (Client: _inner, AccountUsername: accountUsername, Destination: destination),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteAsync(
        string accountUsername,
        string backupId,
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.DeleteAsync(
                    state.AccountUsername,
                    state.BackupId,
                    state.Destination,
                    token);
            },
            (Client: _inner, AccountUsername: accountUsername, BackupId: backupId, Destination: destination),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentPublicReadVerdict>> ProbeAsync(
        AgentBackupDestination destination,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.ProbeAsync(state.Destination, token);
            },
            (Client: _inner, Destination: destination),
            cancellationToken);
    }
}
