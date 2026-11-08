using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.DnsService;

/// <summary>Production <see cref="IDnsServiceInvoker"/> backed by the generated gRPC client.</summary>
internal sealed class GrpcDnsServiceInvoker : IDnsServiceInvoker
{
    /// <summary>Where the channel for the server the current call is aimed at comes from.</summary>
    private readonly IAgentChannels _channels;

    /// <summary>Wraps <paramref name="channels"/> behind the <see cref="IDnsServiceInvoker"/> seam.</summary>
    /// <param name="channels">Hands out the channel of the server each call is aimed at.</param>
    public GrpcDnsServiceInvoker(IAgentChannels channels)
    {
        _channels = channels;
    }

    /// <summary>
    /// A generated client over the channel of the server the current call is aimed at, built per use so
    /// the target is read when the call starts, never earlier.
    /// </summary>
    private Maran.Agent.V1.DnsService.DnsServiceClient Client
    {
        get { return new Maran.Agent.V1.DnsService.DnsServiceClient(_channels.ForCurrent()); }
    }

    /// <inheritdoc/>
    public async Task<EnableDnsResponse> EnableDnsAsync(EnableDnsRequest request, CancellationToken cancellationToken)
    {
        return await Client.EnableDnsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DisableDnsResponse> DisableDnsAsync(
        DisableDnsRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DisableDnsAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetDnsStatusResponse> GetDnsStatusAsync(
        GetDnsStatusRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.GetDnsStatusAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<PutZoneResponse> PutZoneAsync(PutZoneRequest request, CancellationToken cancellationToken)
    {
        return await Client.PutZoneAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<DeleteZoneResponse> DeleteZoneAsync(
        DeleteZoneRequest request,
        CancellationToken cancellationToken)
    {
        return await Client.DeleteZoneAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GetZoneResponse> GetZoneAsync(GetZoneRequest request, CancellationToken cancellationToken)
    {
        return await Client.GetZoneAsync(request, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<ListZonesResponse> ListZonesAsync(ListZonesRequest request, CancellationToken cancellationToken)
    {
        return await Client.ListZonesAsync(request, cancellationToken: cancellationToken);
    }
}
