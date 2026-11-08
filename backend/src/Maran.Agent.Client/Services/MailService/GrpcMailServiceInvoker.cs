using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.MailService;

/// <summary>Production <see cref="IMailServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcMailServiceInvoker : IMailServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="IMailServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcMailServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.MailService.MailServiceClient Client
    {
        get { return new Maran.Agent.V1.MailService.MailServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<GetMailStatusResponse> GetMailStatusAsync(
        GetMailStatusRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetMailStatusAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<EnableMailResponse> EnableMailAsync(
        EnableMailRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.EnableMailAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DisableMailResponse> DisableMailAsync(
        DisableMailRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DisableMailAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ReloadMailTlsResponse> ReloadMailTlsAsync(
        ReloadMailTlsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ReloadMailTlsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ConfigureMailDomainResponse> ConfigureMailDomainAsync(
        ConfigureMailDomainRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ConfigureMailDomainAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RemoveMailDomainResponse> RemoveMailDomainAsync(
        RemoveMailDomainRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.RemoveMailDomainAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetMailboxResponse> SetMailboxAsync(
        SetMailboxRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetMailboxAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RemoveMailboxResponse> RemoveMailboxAsync(
        RemoveMailboxRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.RemoveMailboxAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetMailAliasesResponse> SetMailAliasesAsync(
        SetMailAliasesRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetMailAliasesAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetMailboxAutoresponderResponse> SetMailboxAutoresponderAsync(
        SetMailboxAutoresponderRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetMailboxAutoresponderAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SetAccountMailSuspensionResponse> SetAccountMailSuspensionAsync(
        SetAccountMailSuspensionRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SetAccountMailSuspensionAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RemoveAccountMailResponse> RemoveAccountMailAsync(
        RemoveAccountMailRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.RemoveAccountMailAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ListMailboxUsageResponse> ListMailboxUsageAsync(
        ListMailboxUsageRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ListMailboxUsageAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ListMailQueueResponse> ListMailQueueAsync(
        ListMailQueueRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ListMailQueueAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteQueuedMessageResponse> DeleteQueuedMessageAsync(
        DeleteQueuedMessageRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DeleteQueuedMessageAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<FlushMailQueueResponse> FlushMailQueueAsync(
        FlushMailQueueRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.FlushMailQueueAsync(request, cancellationToken: cancellationToken);
    }
}
