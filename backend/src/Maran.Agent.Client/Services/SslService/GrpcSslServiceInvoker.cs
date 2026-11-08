using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.SslService;

/// <summary>Production <see cref="ISslServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcSslServiceInvoker : ISslServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="ISslServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcSslServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.SslService.SslServiceClient Client
    {
        get { return new Maran.Agent.V1.SslService.SslServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<InstallCertificateResponse> InstallCertificateAsync(
        InstallCertificateRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.InstallCertificateAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RemoveCertificateResponse> RemoveCertificateAsync(
        RemoveCertificateRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.RemoveCertificateAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<InstallPanelCertificateResponse> InstallPanelCertificateAsync(
        InstallPanelCertificateRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.InstallPanelCertificateAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RestorePanelSelfSignedResponse> RestorePanelSelfSignedAsync(
        RestorePanelSelfSignedRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.RestorePanelSelfSignedAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<WritePanelChallengeResponse> WritePanelChallengeAsync(
        WritePanelChallengeRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.WritePanelChallengeAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RemovePanelChallengeResponse> RemovePanelChallengeAsync(
        RemovePanelChallengeRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.RemovePanelChallengeAsync(request, cancellationToken: cancellationToken);
    }
}
