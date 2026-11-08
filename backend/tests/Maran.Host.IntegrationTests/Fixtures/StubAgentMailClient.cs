using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MailService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An <see cref="IAgentMailClient"/> that answers every call with success and a running mail stack, for the
/// HTTP-level tests of the Mail module: the agent is a separate root process that installs Postfix and
/// Dovecot, which cannot be present, and what these tests exercise is the panel's authorisation and tenant
/// scoping in front of it.
/// </summary>
public sealed class StubAgentMailClient : IAgentMailClient
{
    /// <summary>The status every status-shaped call answers with: enabled, TLS in use, Argon2id verified.</summary>
    private static readonly AgentMailStatus Running = new(
        [],
        AgentMailObservation.Yes,
        "mail.example.com",
        AgentMailObservation.Yes,
        2,
        4,
        AgentMailPasswordScheme.Argon2id,
        AgentMailObservation.Yes,
        0,
        AgentMailObservation.Yes);

    /// <inheritdoc />
    public Task<Result<AgentMailStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AgentMailStatus>.Ok(Running));
    }

    /// <inheritdoc />
    public Task<Result<AgentMailStatus>> EnableAsync(
        AgentMailServerSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AgentMailStatus>.Ok(Running));
    }

    /// <inheritdoc />
    public Task<Result<AgentMailStatus>> DisableAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AgentMailStatus>.Ok(Running));
    }

    /// <inheritdoc />
    public Task<Result<AgentMailStatus>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AgentMailStatus>.Ok(Running));
    }

    /// <inheritdoc />
    public Task<Result<bool>> ConfigureDomainAsync(
        AgentMailDomainArguments arguments,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Ok(true));
    }

    /// <inheritdoc />
    public Task<Result> RemoveDomainAsync(string accountUsername, string domain, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result<AgentMailboxChange>> SetMailboxAsync(
        AgentMailboxArguments arguments,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AgentMailboxChange>.Ok(new AgentMailboxChange(true, true)));
    }

    /// <inheritdoc />
    public Task<Result<bool>> RemoveMailboxAsync(
        string accountUsername,
        string domain,
        string localPart,
        bool deleteData,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Ok(deleteData));
    }

    /// <inheritdoc />
    public Task<Result<AgentMailAliasOutcome>> SetAliasesAsync(
        string accountUsername,
        string domain,
        IReadOnlyList<AgentMailAlias> aliases,
        IReadOnlyList<string>? catchAllTargets,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AgentMailAliasOutcome>.Ok(
            new AgentMailAliasOutcome((uint)aliases.Count, catchAllTargets is not null)));
    }

    /// <inheritdoc />
    public Task<Result<bool>> SetAutoresponderAsync(
        string accountUsername,
        string domain,
        string localPart,
        AgentMailAutoresponder? autoresponder,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<bool>.Ok(autoresponder is not null));
    }

    /// <inheritdoc />
    public Task<Result<AgentMailSuspensionOutcome>> SetAccountSuspensionAsync(
        string accountUsername,
        bool suspended,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AgentMailSuspensionOutcome>.Ok(new AgentMailSuspensionOutcome(0, 0)));
    }

    /// <inheritdoc />
    public Task<Result<AgentAccountMailRemoval>> RemoveAccountMailAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AgentAccountMailRemoval>.Ok(new AgentAccountMailRemoval(0, 0)));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<AgentMailboxUsage>>> ListMailboxUsageAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<IReadOnlyList<AgentMailboxUsage>>.Ok([]));
    }

    /// <inheritdoc />
    public Task<Result<AgentMailQueueListing>> ListQueueAsync(uint limit, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<AgentMailQueueListing>.Ok(new AgentMailQueueListing([], 0, false)));
    }

    /// <inheritdoc />
    public Task<Result> DeleteQueuedMessageAsync(string queueId, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result<uint?>> FlushQueueAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<uint?>.Ok(0u));
    }
}
