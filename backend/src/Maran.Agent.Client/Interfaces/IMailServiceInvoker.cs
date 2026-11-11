using Maran.Agent.V1;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// Seam between <see cref="Services.MailService.AgentMailClient"/> and the transport that performs
/// the <c>MailService</c> calls, so the response-to-<see cref="Maran.SharedKernel.Results.Result{T}"/>
/// mapping is testable without a real gRPC channel.
/// </summary>
internal interface IMailServiceInvoker
{
    /// <summary>Invokes <c>GetMailStatus</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<GetMailStatusResponse> GetMailStatusAsync(
        GetMailStatusRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>EnableMail</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<EnableMailResponse> EnableMailAsync(
        EnableMailRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>DisableMail</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DisableMailResponse> DisableMailAsync(
        DisableMailRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ReloadMailTls</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ReloadMailTlsResponse> ReloadMailTlsAsync(
        ReloadMailTlsRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ConfigureMailDomain</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ConfigureMailDomainResponse> ConfigureMailDomainAsync(
        ConfigureMailDomainRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>RemoveMailDomain</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<RemoveMailDomainResponse> RemoveMailDomainAsync(
        RemoveMailDomainRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>SetMailbox</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<SetMailboxResponse> SetMailboxAsync(
        SetMailboxRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>RemoveMailbox</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<RemoveMailboxResponse> RemoveMailboxAsync(
        RemoveMailboxRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>SetMailAliases</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<SetMailAliasesResponse> SetMailAliasesAsync(
        SetMailAliasesRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>SetMailboxAutoresponder</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<SetMailboxAutoresponderResponse> SetMailboxAutoresponderAsync(
        SetMailboxAutoresponderRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>SetAccountMailSuspension</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<SetAccountMailSuspensionResponse> SetAccountMailSuspensionAsync(
        SetAccountMailSuspensionRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>RemoveAccountMail</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<RemoveAccountMailResponse> RemoveAccountMailAsync(
        RemoveAccountMailRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ListMailboxUsage</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ListMailboxUsageResponse> ListMailboxUsageAsync(
        ListMailboxUsageRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>ListMailQueue</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<ListMailQueueResponse> ListMailQueueAsync(
        ListMailQueueRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>DeleteQueuedMessage</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<DeleteQueuedMessageResponse> DeleteQueuedMessageAsync(
        DeleteQueuedMessageRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>FlushMailQueue</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<FlushMailQueueResponse> FlushMailQueueAsync(
        FlushMailQueueRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>SetLocalSubmission</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<SetLocalSubmissionResponse> SetLocalSubmissionAsync(
        SetLocalSubmissionRequest request,
        CancellationToken cancellationToken);

    /// <summary>Invokes <c>RemoveLocalSubmission</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancellation for the call.</param>
    /// <returns>The raw wire response, carrying either branch of its oneof.</returns>
    Task<RemoveLocalSubmissionResponse> RemoveLocalSubmissionAsync(
        RemoveLocalSubmissionRequest request,
        CancellationToken cancellationToken);
}
