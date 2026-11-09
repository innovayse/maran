using System.Runtime.CompilerServices;
using Grpc.Core;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.PhpService;

/// <summary>Production <see cref="IPhpServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcPhpServiceInvoker : IPhpServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="IPhpServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcPhpServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.PhpService.PhpServiceClient Client
    {
        get { return new Maran.Agent.V1.PhpService.PhpServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<ListPhpVersionsResponse> ListPhpVersionsAsync(
        ListPhpVersionsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ListPhpVersionsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ListInstallablePhpVersionsResponse> ListInstallablePhpVersionsAsync(
        ListInstallablePhpVersionsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ListInstallablePhpVersionsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<InstallPhpVersionResponse> InstallPhpVersionAsync(
        InstallPhpVersionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = Client.InstallPhpVersion(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }

    /// <inheritdoc/>
    public async Task<ListPhpExtensionsResponse> ListPhpExtensionsAsync(
        ListPhpExtensionsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ListPhpExtensionsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<SetPhpExtensionResponse> SetPhpExtensionAsync(
        SetPhpExtensionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = Client.SetPhpExtension(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }

    /// <inheritdoc/>
    public async Task<ApplyPhpPoolSettingsResponse> ApplyPhpPoolSettingsAsync(
        ApplyPhpPoolSettingsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ApplyPhpPoolSettingsAsync(request, cancellationToken: cancellationToken);
    }
}
