using System.Runtime.CompilerServices;
using Grpc.Core;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.SitesService;

/// <summary>Production <see cref="ISitesServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcSitesServiceInvoker : ISitesServiceInvoker
{
    /// <summary>The generated gRPC client this adapter wraps.</summary>
    private readonly Maran.Agent.V1.SitesService.SitesServiceClient _client;

    /// <summary>Wraps <paramref name="client"/> behind the <see cref="ISitesServiceInvoker"/> seam.</summary>
    /// <param name="client">The generated client to delegate calls to.</param>
    public GrpcSitesServiceInvoker(Maran.Agent.V1.SitesService.SitesServiceClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public async Task<CreateSiteResponse> CreateSiteAsync(
        CreateSiteRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.CreateSiteAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UpdateSitePhpVersionResponse> UpdateSitePhpVersionAsync(
        UpdateSitePhpVersionRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.UpdateSitePhpVersionAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<EnableSiteResponse> EnableSiteAsync(
        EnableSiteRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.EnableSiteAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DisableSiteResponse> DisableSiteAsync(
        DisableSiteRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.DisableSiteAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteSiteResponse> DeleteSiteAsync(
        DeleteSiteRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.DeleteSiteAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ReloadWebServerResponse> ReloadWebServerAsync(
        ReloadWebServerRequest request,
        CancellationToken cancellationToken)
    {
        return await _client.ReloadWebServerAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TailSiteLogResponse> TailSiteLogAsync(
        TailSiteLogRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var call = _client.TailSiteLog(request, cancellationToken: cancellationToken);

        await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
        {
            yield return response;
        }
    }
}
