using System.Runtime.CompilerServices;
using Grpc.Core;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.AppsService;

/// <summary>Production <see cref="IAppsServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcAppsServiceInvoker : IAppsServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="IAppsServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcAppsServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use, so
    /// one invoker serves every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.AppsService.AppsServiceClient Client
    {
        get { return new Maran.Agent.V1.AppsService.AppsServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<ListAppsResponse> ListAppsAsync(ListAppsRequest request, CancellationToken cancellationToken)
    {
        return await Client.ListAppsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ListInstallableAppsResponse> ListInstallableAppsAsync(
        ListInstallableAppsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ListInstallableAppsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<InstallAppResponse> InstallAppAsync(
        InstallAppRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = Client.InstallApp(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<UninstallAppResponse> UninstallAppAsync(
        UninstallAppRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = Client.UninstallApp(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }

    /// <inheritdoc/>
    public async Task<ControlAppServiceResponse> ControlAppServiceAsync(
        ControlAppServiceRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ControlAppServiceAsync(request, cancellationToken: cancellationToken);
    }
}
