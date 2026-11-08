using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.MailService;

/// <summary>Stub of <c>IMailServiceInvoker</c> returning canned responses and keeping every request.</summary>
/// <remarks>
/// Every captured request here is read by a test: a recorded request nothing asserts makes the mapping
/// look covered while every field of it is free to change. A response left at its default carries
/// neither branch of the oneof, which is how the tests ask for the refusal of an empty answer.
/// </remarks>
internal sealed class StubMailService : IMailServiceInvoker
{
    /// <summary>Response returned from <see cref="GetMailStatusAsync"/>.</summary>
    public GetMailStatusResponse GetMailStatusResponse { get; set; } = new();

    /// <summary>The last <c>GetMailStatus</c> request the stub received, for asserting the mapping.</summary>
    public GetMailStatusRequest? LastGetMailStatusRequest { get; private set; }

    /// <summary>Response returned from <see cref="EnableMailAsync"/>.</summary>
    public EnableMailResponse EnableMailResponse { get; set; } = new();

    /// <summary>The last <c>EnableMail</c> request the stub received, for asserting the mapping.</summary>
    public EnableMailRequest? LastEnableMailRequest { get; private set; }

    /// <summary>Response returned from <see cref="DisableMailAsync"/>.</summary>
    public DisableMailResponse DisableMailResponse { get; set; } = new();

    /// <summary>The last <c>DisableMail</c> request the stub received, for asserting the mapping.</summary>
    public DisableMailRequest? LastDisableMailRequest { get; private set; }

    /// <summary>Response returned from <see cref="ReloadMailTlsAsync"/>.</summary>
    public ReloadMailTlsResponse ReloadMailTlsResponse { get; set; } = new();

    /// <summary>The last <c>ReloadMailTls</c> request the stub received, for asserting the mapping.</summary>
    public ReloadMailTlsRequest? LastReloadMailTlsRequest { get; private set; }

    /// <summary>Response returned from <see cref="ConfigureMailDomainAsync"/>.</summary>
    public ConfigureMailDomainResponse ConfigureMailDomainResponse { get; set; } = new();

    /// <summary>The last <c>ConfigureMailDomain</c> request the stub received, for asserting the mapping.</summary>
    public ConfigureMailDomainRequest? LastConfigureMailDomainRequest { get; private set; }

    /// <summary>Response returned from <see cref="RemoveMailDomainAsync"/>.</summary>
    public RemoveMailDomainResponse RemoveMailDomainResponse { get; set; } = new();

    /// <summary>The last <c>RemoveMailDomain</c> request the stub received, for asserting the mapping.</summary>
    public RemoveMailDomainRequest? LastRemoveMailDomainRequest { get; private set; }

    /// <summary>Response returned from <see cref="SetMailboxAsync"/>.</summary>
    public SetMailboxResponse SetMailboxResponse { get; set; } = new();

    /// <summary>The last <c>SetMailbox</c> request the stub received, for asserting the mapping.</summary>
    public SetMailboxRequest? LastSetMailboxRequest { get; private set; }

    /// <summary>Response returned from <see cref="RemoveMailboxAsync"/>.</summary>
    public RemoveMailboxResponse RemoveMailboxResponse { get; set; } = new();

    /// <summary>The last <c>RemoveMailbox</c> request the stub received, for asserting the mapping.</summary>
    public RemoveMailboxRequest? LastRemoveMailboxRequest { get; private set; }

    /// <summary>Response returned from <see cref="SetMailAliasesAsync"/>.</summary>
    public SetMailAliasesResponse SetMailAliasesResponse { get; set; } = new();

    /// <summary>The last <c>SetMailAliases</c> request the stub received, for asserting the mapping.</summary>
    public SetMailAliasesRequest? LastSetMailAliasesRequest { get; private set; }

    /// <summary>Response returned from <see cref="SetMailboxAutoresponderAsync"/>.</summary>
    public SetMailboxAutoresponderResponse SetMailboxAutoresponderResponse { get; set; } = new();

    /// <summary>The last <c>SetMailboxAutoresponder</c> request the stub received, for asserting the mapping.</summary>
    public SetMailboxAutoresponderRequest? LastSetMailboxAutoresponderRequest { get; private set; }

    /// <summary>Response returned from <see cref="SetAccountMailSuspensionAsync"/>.</summary>
    public SetAccountMailSuspensionResponse SetAccountMailSuspensionResponse { get; set; } = new();

    /// <summary>The last <c>SetAccountMailSuspension</c> request the stub received, for asserting the mapping.</summary>
    public SetAccountMailSuspensionRequest? LastSetAccountMailSuspensionRequest { get; private set; }

    /// <summary>Response returned from <see cref="RemoveAccountMailAsync"/>.</summary>
    public RemoveAccountMailResponse RemoveAccountMailResponse { get; set; } = new();

    /// <summary>The last <c>RemoveAccountMail</c> request the stub received, for asserting the mapping.</summary>
    public RemoveAccountMailRequest? LastRemoveAccountMailRequest { get; private set; }

    /// <summary>Response returned from <see cref="ListMailboxUsageAsync"/>.</summary>
    public ListMailboxUsageResponse ListMailboxUsageResponse { get; set; } = new();

    /// <summary>The last <c>ListMailboxUsage</c> request the stub received, for asserting the mapping.</summary>
    public ListMailboxUsageRequest? LastListMailboxUsageRequest { get; private set; }

    /// <summary>Response returned from <see cref="ListMailQueueAsync"/>.</summary>
    public ListMailQueueResponse ListMailQueueResponse { get; set; } = new();

    /// <summary>The last <c>ListMailQueue</c> request the stub received, for asserting the mapping.</summary>
    public ListMailQueueRequest? LastListMailQueueRequest { get; private set; }

    /// <summary>Response returned from <see cref="DeleteQueuedMessageAsync"/>.</summary>
    public DeleteQueuedMessageResponse DeleteQueuedMessageResponse { get; set; } = new();

    /// <summary>The last <c>DeleteQueuedMessage</c> request the stub received, for asserting the mapping.</summary>
    public DeleteQueuedMessageRequest? LastDeleteQueuedMessageRequest { get; private set; }

    /// <summary>Response returned from <see cref="FlushMailQueueAsync"/>.</summary>
    public FlushMailQueueResponse FlushMailQueueResponse { get; set; } = new();

    /// <summary>The last <c>FlushMailQueue</c> request the stub received, for asserting the mapping.</summary>
    public FlushMailQueueRequest? LastFlushMailQueueRequest { get; private set; }

    /// <inheritdoc/>
    public Task<GetMailStatusResponse> GetMailStatusAsync(GetMailStatusRequest request, CancellationToken cancellationToken)
    {
        LastGetMailStatusRequest = request;

        return Task.FromResult(GetMailStatusResponse);
    }

    /// <inheritdoc/>
    public Task<EnableMailResponse> EnableMailAsync(EnableMailRequest request, CancellationToken cancellationToken)
    {
        LastEnableMailRequest = request;

        return Task.FromResult(EnableMailResponse);
    }

    /// <inheritdoc/>
    public Task<DisableMailResponse> DisableMailAsync(DisableMailRequest request, CancellationToken cancellationToken)
    {
        LastDisableMailRequest = request;

        return Task.FromResult(DisableMailResponse);
    }

    /// <inheritdoc/>
    public Task<ReloadMailTlsResponse> ReloadMailTlsAsync(ReloadMailTlsRequest request, CancellationToken cancellationToken)
    {
        LastReloadMailTlsRequest = request;

        return Task.FromResult(ReloadMailTlsResponse);
    }

    /// <inheritdoc/>
    public Task<ConfigureMailDomainResponse> ConfigureMailDomainAsync(ConfigureMailDomainRequest request, CancellationToken cancellationToken)
    {
        LastConfigureMailDomainRequest = request;

        return Task.FromResult(ConfigureMailDomainResponse);
    }

    /// <inheritdoc/>
    public Task<RemoveMailDomainResponse> RemoveMailDomainAsync(RemoveMailDomainRequest request, CancellationToken cancellationToken)
    {
        LastRemoveMailDomainRequest = request;

        return Task.FromResult(RemoveMailDomainResponse);
    }

    /// <inheritdoc/>
    public Task<SetMailboxResponse> SetMailboxAsync(SetMailboxRequest request, CancellationToken cancellationToken)
    {
        LastSetMailboxRequest = request;

        return Task.FromResult(SetMailboxResponse);
    }

    /// <inheritdoc/>
    public Task<RemoveMailboxResponse> RemoveMailboxAsync(RemoveMailboxRequest request, CancellationToken cancellationToken)
    {
        LastRemoveMailboxRequest = request;

        return Task.FromResult(RemoveMailboxResponse);
    }

    /// <inheritdoc/>
    public Task<SetMailAliasesResponse> SetMailAliasesAsync(SetMailAliasesRequest request, CancellationToken cancellationToken)
    {
        LastSetMailAliasesRequest = request;

        return Task.FromResult(SetMailAliasesResponse);
    }

    /// <inheritdoc/>
    public Task<SetMailboxAutoresponderResponse> SetMailboxAutoresponderAsync(SetMailboxAutoresponderRequest request, CancellationToken cancellationToken)
    {
        LastSetMailboxAutoresponderRequest = request;

        return Task.FromResult(SetMailboxAutoresponderResponse);
    }

    /// <inheritdoc/>
    public Task<SetAccountMailSuspensionResponse> SetAccountMailSuspensionAsync(SetAccountMailSuspensionRequest request, CancellationToken cancellationToken)
    {
        LastSetAccountMailSuspensionRequest = request;

        return Task.FromResult(SetAccountMailSuspensionResponse);
    }

    /// <inheritdoc/>
    public Task<RemoveAccountMailResponse> RemoveAccountMailAsync(RemoveAccountMailRequest request, CancellationToken cancellationToken)
    {
        LastRemoveAccountMailRequest = request;

        return Task.FromResult(RemoveAccountMailResponse);
    }

    /// <inheritdoc/>
    public Task<ListMailboxUsageResponse> ListMailboxUsageAsync(ListMailboxUsageRequest request, CancellationToken cancellationToken)
    {
        LastListMailboxUsageRequest = request;

        return Task.FromResult(ListMailboxUsageResponse);
    }

    /// <inheritdoc/>
    public Task<ListMailQueueResponse> ListMailQueueAsync(ListMailQueueRequest request, CancellationToken cancellationToken)
    {
        LastListMailQueueRequest = request;

        return Task.FromResult(ListMailQueueResponse);
    }

    /// <inheritdoc/>
    public Task<DeleteQueuedMessageResponse> DeleteQueuedMessageAsync(DeleteQueuedMessageRequest request, CancellationToken cancellationToken)
    {
        LastDeleteQueuedMessageRequest = request;

        return Task.FromResult(DeleteQueuedMessageResponse);
    }

    /// <inheritdoc/>
    public Task<FlushMailQueueResponse> FlushMailQueueAsync(FlushMailQueueRequest request, CancellationToken cancellationToken)
    {
        LastFlushMailQueueRequest = request;

        return Task.FromResult(FlushMailQueueResponse);
    }
}
