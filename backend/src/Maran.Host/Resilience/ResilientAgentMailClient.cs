using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MailService;
using Maran.SharedKernel.Results;
using Polly;
using Polly.Registry;

namespace Maran.Host.Resilience;

/// <summary>
/// Puts every agent mail operation through <see cref="AgentOperationPipeline"/>, for the reason
/// <see cref="ResilientAgentAccountsClient"/> gives: without the decorator the call has no timeout at
/// all, and a stuck unix socket hangs the HTTP request that made it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a retry is safe on every one of these.</b> The pipeline retries exactly one thing — a
/// connection to the agent's socket that could not be established — and never a timeout, because the
/// panel's timeout does not stop the agent's work, only its watching of it. A refused connection
/// delivered no request, so the second attempt is the FIRST attempt at the operation. That matters most
/// on the calls where a second overlapping copy would be visible: enabling the stack (four daemons
/// rendered and started), disabling it, reloading TLS, and the account-wide suspension and removal,
/// which end sessions and delete mail.
/// </para>
/// <para>
/// A call that reached the agent is attempted once and answered as a typed failure
/// (<c>AgentUnavailable</c> when the agent stopped answering) without a second copy beside it. The
/// agent runs every mutating mail call to completion even when this side stops waiting, so an abandoned
/// call is not a call that did not happen: the remedy is to ask again, and every call is idempotent.
/// </para>
/// <para>
/// Every method is decorated, including the read-only ones: a status read that hung would hold the
/// settings screen open exactly as a write would.
/// </para>
/// </remarks>
public sealed class ResilientAgentMailClient : IAgentMailClient
{
    /// <summary>The client that actually talks to the agent; this type only adds the policy.</summary>
    private readonly IAgentMailClient _inner;

    /// <summary>The named operation pipeline every call below is executed through.</summary>
    private readonly ResiliencePipeline _pipeline;

    /// <summary>Wraps the real client with the named operation pipeline.</summary>
    /// <param name="inner">The client that actually talks to the agent.</param>
    /// <param name="pipelines">The registry the named pipeline is resolved from.</param>
    public ResilientAgentMailClient(IAgentMailClient inner, ResiliencePipelineProvider<string> pipelines)
    {
        _inner = inner;
        _pipeline = pipelines.GetPipeline(AgentOperationPipeline.Name);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailStatus>> GetStatusAsync(CancellationToken cancellationToken)
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
    public async Task<Result<AgentMailStatus>> EnableAsync(
        AgentMailServerSettings settings,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.EnableAsync(state.Settings, token);
            },
            (Client: _inner, Settings: settings),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailStatus>> DisableAsync(CancellationToken cancellationToken)
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
    public async Task<Result<AgentMailStatus>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.ReloadTlsAsync(token);
            },
            _inner,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> ConfigureDomainAsync(
        AgentMailDomainArguments arguments,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.ConfigureDomainAsync(state.Arguments, token);
            },
            (Client: _inner, Arguments: arguments),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result> RemoveDomainAsync(
        string accountUsername,
        string domain,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.RemoveDomainAsync(state.AccountUsername, state.Domain, token);
            },
            (Client: _inner, AccountUsername: accountUsername, Domain: domain),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailboxChange>> SetMailboxAsync(
        AgentMailboxArguments arguments,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.SetMailboxAsync(state.Arguments, token);
            },
            (Client: _inner, Arguments: arguments),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> RemoveMailboxAsync(
        string accountUsername,
        string domain,
        string localPart,
        bool deleteData,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.RemoveMailboxAsync(
                    state.AccountUsername, state.Domain, state.LocalPart, state.DeleteData, token);
            },
            (Client: _inner,
             AccountUsername: accountUsername,
             Domain: domain,
             LocalPart: localPart,
             DeleteData: deleteData),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailAliasOutcome>> SetAliasesAsync(
        string accountUsername,
        string domain,
        IReadOnlyList<AgentMailAlias> aliases,
        IReadOnlyList<string>? catchAllTargets,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.SetAliasesAsync(
                    state.AccountUsername, state.Domain, state.Aliases, state.CatchAllTargets, token);
            },
            (Client: _inner,
             AccountUsername: accountUsername,
             Domain: domain,
             Aliases: aliases,
             CatchAllTargets: catchAllTargets),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetAutoresponderAsync(
        string accountUsername,
        string domain,
        string localPart,
        AgentMailAutoresponder? autoresponder,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.SetAutoresponderAsync(
                    state.AccountUsername, state.Domain, state.LocalPart, state.Autoresponder, token);
            },
            (Client: _inner,
             AccountUsername: accountUsername,
             Domain: domain,
             LocalPart: localPart,
             Autoresponder: autoresponder),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailSuspensionOutcome>> SetAccountSuspensionAsync(
        string accountUsername,
        bool suspended,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.SetAccountSuspensionAsync(state.AccountUsername, state.Suspended, token);
            },
            (Client: _inner, AccountUsername: accountUsername, Suspended: suspended),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentAccountMailRemoval>> RemoveAccountMailAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.RemoveAccountMailAsync(state.AccountUsername, token);
            },
            (Client: _inner, AccountUsername: accountUsername),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentMailboxUsage>>> ListMailboxUsageAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.ListMailboxUsageAsync(state.AccountUsername, token);
            },
            (Client: _inner, AccountUsername: accountUsername),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailQueueListing>> ListQueueAsync(uint limit, CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.ListQueueAsync(state.Limit, token);
            },
            (Client: _inner, Limit: limit),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteQueuedMessageAsync(string queueId, CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.DeleteQueuedMessageAsync(state.QueueId, token);
            },
            (Client: _inner, QueueId: queueId),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result<uint?>> FlushQueueAsync(CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.FlushQueueAsync(token);
            },
            _inner,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result> SetLocalSubmissionAsync(
        AgentLocalSubmission submission,
        CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.SetLocalSubmissionAsync(state.Submission, token);
            },
            (Client: _inner, Submission: submission),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Result> RemoveLocalSubmissionAsync(string accountUsername, CancellationToken cancellationToken)
    {
        return await AgentOperationPipeline.ExecuteAsync(
            _pipeline,
            async (state, token) =>
            {
                return await state.Client.RemoveLocalSubmissionAsync(state.AccountUsername, token);
            },
            (Client: _inner, AccountUsername: accountUsername),
            cancellationToken);
    }
}
