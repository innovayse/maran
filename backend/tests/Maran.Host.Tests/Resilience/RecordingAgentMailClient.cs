using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.MailService;
using Maran.Host.Tests.Fixtures;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>An inner mail client that records its arguments and can fail or hang on demand.</summary>
/// <remarks>
/// Hanging is the point, as it is for the other recorders: only a call that never returns proves the
/// decorator applies a TIMEOUT rather than merely forwarding.
/// </remarks>
internal sealed class RecordingAgentMailClient : IAgentMailClient
{
    /// <summary>A status the ok paths return; its content is irrelevant to these tests.</summary>
    private static readonly AgentMailStatus Observed = new(
        [],
        AgentMailObservation.Yes,
        "mail.example.test",
        AgentMailObservation.Yes,
        2,
        3,
        AgentMailPasswordScheme.Argon2id,
        AgentMailObservation.Yes,
        0,
        AgentMailObservation.Yes);

    /// <summary>How many calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>When true, every call waits for its cancellation token instead of returning.</summary>
    public bool Hangs { get; set; }

    /// <summary>How many times any method on this client was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>The settings of the last enable call.</summary>
    public AgentMailServerSettings? LastSettings { get; private set; }

    /// <summary>The domain arguments of the last domain call.</summary>
    public AgentMailDomainArguments? LastDomainArguments { get; private set; }

    /// <summary>The mailbox arguments of the last mailbox call.</summary>
    public AgentMailboxArguments? LastMailboxArguments { get; private set; }

    /// <summary>The account username of the last call that carried one.</summary>
    public string? LastAccountUsername { get; private set; }

    /// <summary>The domain of the last call that carried one.</summary>
    public string? LastDomain { get; private set; }

    /// <summary>The local part of the last call that carried one.</summary>
    public string? LastLocalPart { get; private set; }

    /// <summary>The data flag of the last mailbox removal.</summary>
    public bool LastDeleteData { get; private set; }

    /// <summary>The suspension flag of the last suspension call.</summary>
    public bool LastSuspended { get; private set; }

    /// <summary>The aliases of the last alias call.</summary>
    public IReadOnlyList<AgentMailAlias>? LastAliases { get; private set; }

    /// <summary>The catch-all targets of the last alias call.</summary>
    public IReadOnlyList<string>? LastCatchAllTargets { get; private set; }

    /// <summary>The autoresponder of the last autoresponder call.</summary>
    public AgentMailAutoresponder? LastAutoresponder { get; private set; }

    /// <summary>The limit of the last queue listing.</summary>
    public uint LastLimit { get; private set; }

    /// <summary>The queue id of the last queue deletion.</summary>
    public string? LastQueueId { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<AgentMailStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<AgentMailStatus>.Ok(Observed);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailStatus>> EnableAsync(
        AgentMailServerSettings settings,
        CancellationToken cancellationToken)
    {
        LastSettings = settings;

        await EnterAsync(cancellationToken);

        return Result<AgentMailStatus>.Ok(Observed);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailStatus>> DisableAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<AgentMailStatus>.Ok(Observed);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailStatus>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<AgentMailStatus>.Ok(Observed);
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> ConfigureDomainAsync(
        AgentMailDomainArguments arguments,
        CancellationToken cancellationToken)
    {
        LastDomainArguments = arguments;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(true);
    }

    /// <inheritdoc/>
    public async Task<Result> RemoveDomainAsync(
        string accountUsername,
        string domain,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDomain = domain;

        await EnterAsync(cancellationToken);

        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailboxChange>> SetMailboxAsync(
        AgentMailboxArguments arguments,
        CancellationToken cancellationToken)
    {
        LastMailboxArguments = arguments;

        await EnterAsync(cancellationToken);

        return Result<AgentMailboxChange>.Ok(new AgentMailboxChange(true, true));
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> RemoveMailboxAsync(
        string accountUsername,
        string domain,
        string localPart,
        bool deleteData,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDomain = domain;
        LastLocalPart = localPart;
        LastDeleteData = deleteData;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(deleteData);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailAliasOutcome>> SetAliasesAsync(
        string accountUsername,
        string domain,
        IReadOnlyList<AgentMailAlias> aliases,
        IReadOnlyList<string>? catchAllTargets,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDomain = domain;
        LastAliases = aliases;
        LastCatchAllTargets = catchAllTargets;

        await EnterAsync(cancellationToken);

        return Result<AgentMailAliasOutcome>.Ok(new AgentMailAliasOutcome((uint)aliases.Count, catchAllTargets is not null));
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetAutoresponderAsync(
        string accountUsername,
        string domain,
        string localPart,
        AgentMailAutoresponder? autoresponder,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastDomain = domain;
        LastLocalPart = localPart;
        LastAutoresponder = autoresponder;

        await EnterAsync(cancellationToken);

        return Result<bool>.Ok(autoresponder is not null);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailSuspensionOutcome>> SetAccountSuspensionAsync(
        string accountUsername,
        bool suspended,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;
        LastSuspended = suspended;

        await EnterAsync(cancellationToken);

        return Result<AgentMailSuspensionOutcome>.Ok(new AgentMailSuspensionOutcome(1, 0));
    }

    /// <inheritdoc/>
    public async Task<Result<AgentAccountMailRemoval>> RemoveAccountMailAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;

        await EnterAsync(cancellationToken);

        return Result<AgentAccountMailRemoval>.Ok(new AgentAccountMailRemoval(1, 1));
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentMailboxUsage>>> ListMailboxUsageAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;

        await EnterAsync(cancellationToken);

        return Result<IReadOnlyList<AgentMailboxUsage>>.Ok([]);
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailQueueListing>> ListQueueAsync(uint limit, CancellationToken cancellationToken)
    {
        LastLimit = limit;

        await EnterAsync(cancellationToken);

        return Result<AgentMailQueueListing>.Ok(new AgentMailQueueListing([], 0, false));
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteQueuedMessageAsync(string queueId, CancellationToken cancellationToken)
    {
        LastQueueId = queueId;

        await EnterAsync(cancellationToken);

        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<Result<uint?>> FlushQueueAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<uint?>.Ok(0u);
    }

    /// <summary>The last local submission binding received.</summary>
    public AgentLocalSubmission? LastSubmission { get; private set; }

    /// <inheritdoc/>
    public async Task<Result> SetLocalSubmissionAsync(AgentLocalSubmission submission, CancellationToken cancellationToken)
    {
        LastSubmission = submission;

        await EnterAsync(cancellationToken);

        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<Result> RemoveLocalSubmissionAsync(string accountUsername, CancellationToken cancellationToken)
    {
        LastAccountUsername = accountUsername;

        await EnterAsync(cancellationToken);

        return Result.Ok();
    }

    /// <summary>Counts the call and applies whichever misbehaviour the test asked for.</summary>
    /// <param name="cancellationToken">The token the pipeline's timeout cancels.</param>
    /// <returns>A task that completes once the call may return.</returns>
    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw AgentTransportFaults.ConnectRefused();
        }

        if (Hangs)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        await Task.Yield();
    }
}
