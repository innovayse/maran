using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MailService;
using Maran.Agent.Simulated.Errors;

namespace Maran.Agent.Simulated.Services.MailService;

/// <summary>
/// The agent's mail operations on a simulated server, where mail is never enabled: no daemon is
/// installed and nothing listens.
/// </summary>
/// <remarks>
/// The status says plainly that the stack is not installed, and every call that would need a running
/// stack answers that the agent does not perform it here. The calls an account's life depends on
/// (suspending, resuming and removing the account's mail) answer that there is nothing to do, so an
/// account can be suspended and deleted on a simulated server as on a real one that never enabled mail.
/// </remarks>
public sealed class SimulatedAgentMailClient : IAgentMailClient
{
    /// <inheritdoc/>
    public Task<Result<AgentMailStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<AgentMailStatus>.Ok(NotInstalled()));
    }

    /// <inheritdoc/>
    public Task<Result<AgentMailStatus>> EnableAsync(
        AgentMailServerSettings settings,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<AgentMailStatus>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    public Task<Result<AgentMailStatus>> DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<AgentMailStatus>.Ok(NotInstalled()));
    }

    /// <inheritdoc/>
    public Task<Result<AgentMailStatus>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<AgentMailStatus>.Fail(SimulatedAgentErrors.NotFound()));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> ConfigureDomainAsync(
        AgentMailDomainArguments arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<bool>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    public Task<Result> RemoveDomainAsync(
        string accountUsername,
        string domain,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result.Fail(SimulatedAgentErrors.NotFound()));
    }

    /// <inheritdoc/>
    public Task<Result<AgentMailboxChange>> SetMailboxAsync(
        AgentMailboxArguments arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<AgentMailboxChange>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> RemoveMailboxAsync(
        string accountUsername,
        string domain,
        string localPart,
        bool deleteData,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<bool>.Fail(SimulatedAgentErrors.NotFound()));
    }

    /// <inheritdoc/>
    public Task<Result<AgentMailAliasOutcome>> SetAliasesAsync(
        string accountUsername,
        string domain,
        IReadOnlyList<AgentMailAlias> aliases,
        IReadOnlyList<string>? catchAllTargets,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<AgentMailAliasOutcome>.Fail(SimulatedAgentErrors.NotImplemented()));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> SetAutoresponderAsync(
        string accountUsername,
        string domain,
        string localPart,
        AgentMailAutoresponder? autoresponder,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<bool>.Fail(SimulatedAgentErrors.NotFound()));
    }

    /// <inheritdoc/>
    public Task<Result<AgentMailSuspensionOutcome>> SetAccountSuspensionAsync(
        string accountUsername,
        bool suspended,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<AgentMailSuspensionOutcome>.Ok(new AgentMailSuspensionOutcome(0, 0)));
    }

    /// <inheritdoc/>
    public Task<Result<AgentAccountMailRemoval>> RemoveAccountMailAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<AgentAccountMailRemoval>.Ok(new AgentAccountMailRemoval(0, 0)));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AgentMailboxUsage>>> ListMailboxUsageAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<AgentMailboxUsage> none = [];

        return Task.FromResult(Result<IReadOnlyList<AgentMailboxUsage>>.Ok(none));
    }

    /// <inheritdoc/>
    public Task<Result<AgentMailQueueListing>> ListQueueAsync(uint limit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<AgentMailQueueListing>.Ok(new AgentMailQueueListing([], 0, false)));
    }

    /// <inheritdoc/>
    public Task<Result> DeleteQueuedMessageAsync(string queueId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result.Fail(SimulatedAgentErrors.NotFound()));
    }

    /// <inheritdoc/>
    public Task<Result<uint?>> FlushQueueAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<uint?>.Ok(0u));
    }

    /// <summary>The status of a host where the mail stack is not installed.</summary>
    /// <returns>No daemon, nothing enabled, nothing observed that was not observed to be absent.</returns>
    private static AgentMailStatus NotInstalled()
    {
        return new AgentMailStatus(
            [],
            AgentMailObservation.No,
            string.Empty,
            AgentMailObservation.No,
            null,
            null,
            null,
            AgentMailObservation.NotObserved,
            0,
            AgentMailObservation.No);
    }

    /// <inheritdoc/>
    public Task<Result> SetLocalSubmissionAsync(AgentLocalSubmission submission, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc/>
    public Task<Result> RemoveLocalSubmissionAsync(string accountUsername, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result.Ok());
    }
}
