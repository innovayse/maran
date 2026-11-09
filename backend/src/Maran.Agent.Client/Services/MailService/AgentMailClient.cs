using Grpc.Net.Client;
using Maran.Agent.Client.Channels;
using Maran.Agent.Client.Errors;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Resources;
using Maran.Agent.V1;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Maran.Agent.Client.Services.MailService;

/// <summary>Maps the agent's mail rpcs onto <see cref="Result{T}"/>.</summary>
/// <remarks>
/// <para>
/// Same shape as <see cref="FtpsService.AgentFtpsClient"/>, deliberately: the failure branch of the
/// response oneof becomes a typed <see cref="Error"/> carrying only a code, and the agent's own
/// diagnostic text — which can name configuration files, the certificate and queue ids — is logged
/// rather than returned (rules/security.md item 8). A response with neither branch set is refused
/// rather than read as success, and so is an ok whose payload cannot be read in full.
/// </para>
/// <para>
/// Every wire error goes through <see cref="AgentErrorTranslator"/>. The three secrets this client sends
/// — the mailbox password hash, the DKIM private key and the webmail master hash — are unwrapped exactly
/// once, onto the wire, and handed to the translator so the agent quoting one back cannot put it in the
/// panel's log.
/// </para>
/// <para>
/// The four lifecycle rpcs answer with four distinct wire messages around the same <c>MailStatus</c>, so
/// each is read through <see cref="ToStatus"/>, which is where an unreadable status becomes
/// <c>AgentInvalidResponse</c> instead of a guess.
/// </para>
/// </remarks>
public sealed class AgentMailClient : IAgentMailClient
{
    /// <summary>The transport seam this client drives; a stub in tests, a real gRPC call in production.</summary>
    private readonly IMailServiceInvoker _invoker;

    /// <summary>Where the agent's own diagnostic text goes, since <see cref="Error"/> carries only a code.</summary>
    private readonly ILogger<AgentMailClient> _logger;

    /// <summary>Creates a client over an explicit transport seam (used by tests and by the other constructors).</summary>
    /// <param name="invoker">The transport that performs the actual calls.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    internal AgentMailClient(IMailServiceInvoker invoker, ILogger<AgentMailClient> logger)
    {
        _invoker = invoker;
        _logger = logger;
    }

    /// <summary>Creates a client whose every call goes to the server the current agent target selects.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentMailClient(IAgentChannels channels, ILogger<AgentMailClient> logger)
        : this(new GrpcMailServiceInvoker(channels), logger)
    {
    }

    /// <summary>Creates a client over one fixed <paramref name="channel"/>, whatever the target.</summary>
    /// <param name="channel">A channel to the agent, e.g. from <see cref="Channels.AgentChannel.CreateUnixSocket"/>.</param>
    /// <param name="logger">Sink for the agent's diagnostic text.</param>
    public AgentMailClient(GrpcChannel channel, ILogger<AgentMailClient> logger)
        : this(new FixedAgentChannels(channel), logger)
    {
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.GetMailStatusAsync(new GetMailStatusRequest(), cancellationToken);

        return response.ResultCase switch
        {
            GetMailStatusResponse.ResultOneofCase.Ok => ToStatus(response.Ok.Status),
            GetMailStatusResponse.ResultOneofCase.Error => AgentMailFailure.Translate<AgentMailStatus>(
                _logger, response.Error, nameof(GetStatusAsync)),
            _ => AgentMailFailure.Invalid<AgentMailStatus>(),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailStatus>> EnableAsync(
        AgentMailServerSettings settings,
        CancellationToken cancellationToken)
    {
        var request = AgentMailRequestMapper.ToEnableRequest(settings);
        var response = await _invoker.EnableMailAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            EnableMailResponse.ResultOneofCase.Ok => ToStatus(response.Ok.Status),
            EnableMailResponse.ResultOneofCase.Error => AgentMailFailure.Translate<AgentMailStatus>(
                _logger, response.Error, nameof(EnableAsync), settings.WebmailMasterHash),
            _ => AgentMailFailure.Invalid<AgentMailStatus>(),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailStatus>> DisableAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.DisableMailAsync(new DisableMailRequest(), cancellationToken);

        return response.ResultCase switch
        {
            DisableMailResponse.ResultOneofCase.Ok => ToStatus(response.Ok.Status),
            DisableMailResponse.ResultOneofCase.Error => AgentMailFailure.Translate<AgentMailStatus>(
                _logger, response.Error, nameof(DisableAsync)),
            _ => AgentMailFailure.Invalid<AgentMailStatus>(),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailStatus>> ReloadTlsAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.ReloadMailTlsAsync(new ReloadMailTlsRequest(), cancellationToken);

        return response.ResultCase switch
        {
            ReloadMailTlsResponse.ResultOneofCase.Ok => ToStatus(response.Ok.Status),
            ReloadMailTlsResponse.ResultOneofCase.Error => AgentMailFailure.Translate<AgentMailStatus>(
                _logger, response.Error, nameof(ReloadTlsAsync)),
            _ => AgentMailFailure.Invalid<AgentMailStatus>(),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> ConfigureDomainAsync(
        AgentMailDomainArguments arguments,
        CancellationToken cancellationToken)
    {
        var request = AgentMailRequestMapper.ToDomainRequest(arguments);
        var response = await _invoker.ConfigureMailDomainAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ConfigureMailDomainResponse.ResultOneofCase.Ok => Result<bool>.Ok(response.Ok.Changed),
            ConfigureMailDomainResponse.ResultOneofCase.Error => AgentMailFailure.Translate<bool>(
                _logger, response.Error, nameof(ConfigureDomainAsync), arguments.DkimPrivateKeyPem),
            _ => AgentMailFailure.Invalid<bool>(),
        };
    }

    /// <inheritdoc/>
    public async Task<Result> RemoveDomainAsync(
        string accountUsername,
        string domain,
        CancellationToken cancellationToken)
    {
        var request = new RemoveMailDomainRequest { AccountUsername = accountUsername, Domain = domain };
        var response = await _invoker.RemoveMailDomainAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            RemoveMailDomainResponse.ResultOneofCase.Ok => Result.Ok(),
            RemoveMailDomainResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(RemoveDomainAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailboxChange>> SetMailboxAsync(
        AgentMailboxArguments arguments,
        CancellationToken cancellationToken)
    {
        var request = AgentMailRequestMapper.ToMailboxRequest(arguments);
        var response = await _invoker.SetMailboxAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SetMailboxResponse.ResultOneofCase.Ok => Result<AgentMailboxChange>.Ok(
                new AgentMailboxChange(response.Ok.Created, response.Ok.PasswordChanged)),
            SetMailboxResponse.ResultOneofCase.Error => AgentMailFailure.Translate<AgentMailboxChange>(
                _logger, response.Error, nameof(SetMailboxAsync), arguments.PasswordHash),
            _ => AgentMailFailure.Invalid<AgentMailboxChange>(),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> RemoveMailboxAsync(
        string accountUsername,
        string domain,
        string localPart,
        bool deleteData,
        CancellationToken cancellationToken)
    {
        var request = new RemoveMailboxRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            LocalPart = localPart,
            DeleteData = deleteData,
        };
        var response = await _invoker.RemoveMailboxAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            RemoveMailboxResponse.ResultOneofCase.Ok => Result<bool>.Ok(response.Ok.DataDeleted),
            RemoveMailboxResponse.ResultOneofCase.Error => AgentMailFailure.Translate<bool>(
                _logger, response.Error, nameof(RemoveMailboxAsync)),
            _ => AgentMailFailure.Invalid<bool>(),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailAliasOutcome>> SetAliasesAsync(
        string accountUsername,
        string domain,
        IReadOnlyList<AgentMailAlias> aliases,
        IReadOnlyList<string>? catchAllTargets,
        CancellationToken cancellationToken)
    {
        var request = AgentMailRequestMapper.ToAliasRequest(accountUsername, domain, aliases, catchAllTargets);
        var response = await _invoker.SetMailAliasesAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SetMailAliasesResponse.ResultOneofCase.Ok => Result<AgentMailAliasOutcome>.Ok(
                new AgentMailAliasOutcome(response.Ok.AliasCount, response.Ok.CatchAllSet)),
            SetMailAliasesResponse.ResultOneofCase.Error => AgentMailFailure.Translate<AgentMailAliasOutcome>(
                _logger, response.Error, nameof(SetAliasesAsync)),
            _ => AgentMailFailure.Invalid<AgentMailAliasOutcome>(),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> SetAutoresponderAsync(
        string accountUsername,
        string domain,
        string localPart,
        AgentMailAutoresponder? autoresponder,
        CancellationToken cancellationToken)
    {
        var request = new SetMailboxAutoresponderRequest
        {
            AccountUsername = accountUsername,
            Domain = domain,
            LocalPart = localPart,

            // An absent message clears the autoresponder; the field is left unset rather than set empty.
            Autoresponder = autoresponder is null ? null : AgentMailRequestMapper.ToWire(autoresponder),
        };

        var response = await _invoker.SetMailboxAutoresponderAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SetMailboxAutoresponderResponse.ResultOneofCase.Ok => Result<bool>.Ok(response.Ok.Installed),
            SetMailboxAutoresponderResponse.ResultOneofCase.Error => AgentMailFailure.Translate<bool>(
                _logger, response.Error, nameof(SetAutoresponderAsync)),
            _ => AgentMailFailure.Invalid<bool>(),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailSuspensionOutcome>> SetAccountSuspensionAsync(
        string accountUsername,
        bool suspended,
        CancellationToken cancellationToken)
    {
        var request = new SetAccountMailSuspensionRequest { AccountUsername = accountUsername, Suspended = suspended };
        var response = await _invoker.SetAccountMailSuspensionAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            SetAccountMailSuspensionResponse.ResultOneofCase.Ok => Result<AgentMailSuspensionOutcome>.Ok(
                new AgentMailSuspensionOutcome(response.Ok.Mailboxes, response.Ok.SessionsEnded)),
            SetAccountMailSuspensionResponse.ResultOneofCase.Error =>
                AgentMailFailure.Translate<AgentMailSuspensionOutcome>(
                    _logger, response.Error, nameof(SetAccountSuspensionAsync)),
            _ => AgentMailFailure.Invalid<AgentMailSuspensionOutcome>(),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentAccountMailRemoval>> RemoveAccountMailAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        var request = new RemoveAccountMailRequest { AccountUsername = accountUsername };
        var response = await _invoker.RemoveAccountMailAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            RemoveAccountMailResponse.ResultOneofCase.Ok => Result<AgentAccountMailRemoval>.Ok(
                new AgentAccountMailRemoval(response.Ok.MailboxesRemoved, response.Ok.DomainsRemoved)),
            RemoveAccountMailResponse.ResultOneofCase.Error => AgentMailFailure.Translate<AgentAccountMailRemoval>(
                _logger, response.Error, nameof(RemoveAccountMailAsync)),
            _ => AgentMailFailure.Invalid<AgentAccountMailRemoval>(),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<AgentMailboxUsage>>> ListMailboxUsageAsync(
        string accountUsername,
        CancellationToken cancellationToken)
    {
        var request = new ListMailboxUsageRequest { AccountUsername = accountUsername };
        var response = await _invoker.ListMailboxUsageAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            ListMailboxUsageResponse.ResultOneofCase.Ok => Result<IReadOnlyList<AgentMailboxUsage>>.Ok(
                response.Ok.Mailboxes.Select(AgentMailUsageMapper.ToUsage).ToList()),
            ListMailboxUsageResponse.ResultOneofCase.Error =>
                AgentMailFailure.Translate<IReadOnlyList<AgentMailboxUsage>>(
                    _logger, response.Error, nameof(ListMailboxUsageAsync)),
            _ => AgentMailFailure.Invalid<IReadOnlyList<AgentMailboxUsage>>(),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<AgentMailQueueListing>> ListQueueAsync(uint limit, CancellationToken cancellationToken)
    {
        var response = await _invoker.ListMailQueueAsync(new ListMailQueueRequest { Limit = limit }, cancellationToken);

        return response.ResultCase switch
        {
            ListMailQueueResponse.ResultOneofCase.Ok => AgentMailQueueMapper.ToQueue(response.Ok) is { } queue
                ? Result<AgentMailQueueListing>.Ok(queue)
                : AgentMailFailure.Invalid<AgentMailQueueListing>(),
            ListMailQueueResponse.ResultOneofCase.Error => AgentMailFailure.Translate<AgentMailQueueListing>(
                _logger, response.Error, nameof(ListQueueAsync)),
            _ => AgentMailFailure.Invalid<AgentMailQueueListing>(),
        };
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteQueuedMessageAsync(string queueId, CancellationToken cancellationToken)
    {
        var request = new DeleteQueuedMessageRequest { QueueId = queueId };
        var response = await _invoker.DeleteQueuedMessageAsync(request, cancellationToken);

        return response.ResultCase switch
        {
            DeleteQueuedMessageResponse.ResultOneofCase.Ok => Result.Ok(),
            DeleteQueuedMessageResponse.ResultOneofCase.Error => Result.Fail(
                AgentErrorTranslator.ToError(_logger, response.Error, nameof(DeleteQueuedMessageAsync))),
            _ => Result.Fail(Error.Of(nameof(ErrorMessages.AgentInvalidResponse), ErrorType.Failure)),
        };
    }

    /// <inheritdoc/>
    public async Task<Result<uint?>> FlushQueueAsync(CancellationToken cancellationToken)
    {
        var response = await _invoker.FlushMailQueueAsync(new FlushMailQueueRequest(), cancellationToken);

        return response.ResultCase switch
        {
            FlushMailQueueResponse.ResultOneofCase.Ok => Result<uint?>.Ok(response.Ok.Queue?.Messages),
            FlushMailQueueResponse.ResultOneofCase.Error => AgentMailFailure.Translate<uint?>(
                _logger, response.Error, nameof(FlushQueueAsync)),
            _ => AgentMailFailure.Invalid<uint?>(),
        };
    }

    /// <summary>Reads a lifecycle rpc's status payload into the panel's shape.</summary>
    /// <param name="status">The wire status, absent when the agent sent an ok without one.</param>
    /// <returns>
    /// The status, or <c>AgentInvalidResponse</c> when it is absent or names a daemon this build does not know.
    /// </returns>
    private static Result<AgentMailStatus> ToStatus(MailStatus? status)
    {
        var mapped = status is null ? null : AgentMailStatusMapper.ToStatus(status);

        return mapped is null ? AgentMailFailure.Invalid<AgentMailStatus>() : Result<AgentMailStatus>.Ok(mapped);
    }
}
