using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DbService;
using Maran.SharedKernel.Results;
using Maran.SharedKernel.Security;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts every agent database operation through <see cref="AgentOperationPipeline"/>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives: without the decorator the call has no timeout
/// at all, and a stuck unix socket hangs the HTTP request that made it.
/// </summary>
/// <remarks>
/// Every method below goes through the pipeline, including the read-only ones. A listing that hangs
/// hangs a request exactly as a creation does, and the defect this repository has already found was
/// one method quietly left undecorated while the class as a whole looked wired.
/// </remarks>
public sealed class ResilientAgentDbClient : IAgentDbClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentDbClient _inner;

    /// <summary>The named operation pipeline every call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentDbClient(IAgentDbClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<Result<CreatedDatabaseDto>> CreateAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.CreateAsync(
                    state.AccountUsername,
                    state.DatabaseName,
                    state.DbUsername,
                    state.Password,
                    token);
            },
            (Client: _inner,
             AccountUsername: accountUsername,
             DatabaseName: databaseName,
             DbUsername: dbUsername,
             Password: password),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DropAsync(
        string accountUsername,
        string databaseName,
        string dbUsername,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.DropAsync(
                    state.AccountUsername, state.DatabaseName, state.DbUsername, token);
            },
            (Client: _inner,
             AccountUsername: accountUsername,
             DatabaseName: databaseName,
             DbUsername: dbUsername),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetPasswordAsync(
        string accountUsername,
        string dbUsername,
        SensitiveString password,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.SetPasswordAsync(
                    state.AccountUsername, state.DbUsername, state.Password, token);
            },
            (Client: _inner,
             AccountUsername: accountUsername,
             DbUsername: dbUsername,
             Password: password),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<DatabaseSummaryDto>>> ListAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.ListAsync(state.AccountUsername, token);
            },
            (Client: _inner, AccountUsername: accountUsername),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<ulong>> GetSizeAsync(
        string accountUsername,
        string databaseName,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.GetSizeAsync(state.AccountUsername, state.DatabaseName, token);
            },
            (Client: _inner, AccountUsername: accountUsername, DatabaseName: databaseName),
            cancellationToken);
    }
}
