using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.CronService;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts every agent cron operation through <see cref="AgentOperationPipeline"/>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives: without the decorator the call has no timeout
/// at all, and a stuck unix socket hangs the HTTP request that made it.
/// </summary>
/// <remarks>
/// Every method below goes through the pipeline, including the read-only ones. A listing that hangs
/// hangs a request exactly as a creation does, and the defect this repository has already found was
/// one method quietly left undecorated while the class as a whole looked wired.
/// </remarks>
public sealed class ResilientAgentCronClient : IAgentCronClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentCronClient _inner;

    /// <summary>The named operation pipeline every call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentCronClient(IAgentCronClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentCronEntry>>> ListEntriesAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.ListEntriesAsync(state.AccountUsername, token);
            },
            (Client: _inner, AccountUsername: accountUsername),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<string>> CreateEntryAsync(
        string accountUsername,
        AgentCronSchedule schedule,
        string command,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.CreateEntryAsync(
                    state.AccountUsername, state.Schedule, state.Command, token);
            },
            (Client: _inner,
             AccountUsername: accountUsername,
             Schedule: schedule,
             Command: command),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> UpdateEntryAsync(
        string accountUsername,
        string entryId,
        AgentCronSchedule schedule,
        string command,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.UpdateEntryAsync(
                    state.AccountUsername, state.EntryId, state.Schedule, state.Command, token);
            },
            (Client: _inner,
             AccountUsername: accountUsername,
             EntryId: entryId,
             Schedule: schedule,
             Command: command),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteEntryAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.DeleteEntryAsync(state.AccountUsername, state.EntryId, token);
            },
            (Client: _inner, AccountUsername: accountUsername, EntryId: entryId),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetEntryEnabledAsync(
        string accountUsername,
        string entryId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.SetEntryEnabledAsync(
                    state.AccountUsername, state.EntryId, state.Enabled, token);
            },
            (Client: _inner,
             AccountUsername: accountUsername,
             EntryId: entryId,
             Enabled: enabled),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentCronRunOutput?>> GetEntryOutputAsync(
        string accountUsername,
        string entryId,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.GetEntryOutputAsync(state.AccountUsername, state.EntryId, token);
            },
            (Client: _inner, AccountUsername: accountUsername, EntryId: entryId),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentCronEnvVar>>> GetEnvironmentAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.GetEnvironmentAsync(state.AccountUsername, token);
            },
            (Client: _inner, AccountUsername: accountUsername),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetEnvironmentAsync(
        string accountUsername,
        IReadOnlyList<AgentCronEnvVar> variables,
        CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(
            async (state, token) =>
            {
                return await state.Client.SetEnvironmentAsync(state.AccountUsername, state.Variables, token);
            },
            (Client: _inner, AccountUsername: accountUsername, Variables: variables),
            cancellationToken);
    }
}
