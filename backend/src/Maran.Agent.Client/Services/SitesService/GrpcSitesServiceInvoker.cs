using System.Runtime.CompilerServices;
using Grpc.Core;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.SitesService;

/// <summary>Production <see cref="ISitesServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcSitesServiceInvoker : ISitesServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="ISitesServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcSitesServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use.
    /// Building it is cheap (it holds the channel and nothing else), and building it per call is what
    /// lets one invoker serve every server: the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.SitesService.SitesServiceClient Client
    {
        get { return new Maran.Agent.V1.SitesService.SitesServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<CreateSiteResponse> CreateSiteAsync(
        CreateSiteRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.CreateSiteAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UpdateSitePhpVersionResponse> UpdateSitePhpVersionAsync(
        UpdateSitePhpVersionRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.UpdateSitePhpVersionAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<EnableSiteResponse> EnableSiteAsync(
        EnableSiteRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.EnableSiteAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DisableSiteResponse> DisableSiteAsync(
        DisableSiteRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DisableSiteAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteSiteResponse> DeleteSiteAsync(
        DeleteSiteRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DeleteSiteAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<SummariseSiteTrafficResponse> SummariseSiteTrafficAsync(
        SummariseSiteTrafficRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.SummariseSiteTrafficAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<PruneSiteLogsResponse> PruneSiteLogsAsync(
        PruneSiteLogsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.PruneSiteLogsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ReloadWebServerResponse> ReloadWebServerAsync(
        ReloadWebServerRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.ReloadWebServerAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TailSiteLogResponse> TailSiteLogAsync(
        TailSiteLogRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = Client.TailSiteLog(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }
}
