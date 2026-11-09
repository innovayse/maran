using Maran.Agent.V1;

namespace Maran.Agent.Client.Interfaces;

/// <summary>
/// The transport seam under <see cref="IAgentDnsClient"/>: one method per rpc of the agent's DNS service,
/// in generated types. Internal because generated wire types may not leave this project.
/// </summary>
internal interface IDnsServiceInvoker
{
    /// <summary>Calls <c>EnableDns</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<EnableDnsResponse> EnableDnsAsync(EnableDnsRequest request, CancellationToken cancellationToken);

    /// <summary>Calls <c>DisableDns</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<DisableDnsResponse> DisableDnsAsync(DisableDnsRequest request, CancellationToken cancellationToken);

    /// <summary>Calls <c>GetDnsStatus</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<GetDnsStatusResponse> GetDnsStatusAsync(GetDnsStatusRequest request, CancellationToken cancellationToken);

    /// <summary>Calls <c>PutZone</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<PutZoneResponse> PutZoneAsync(PutZoneRequest request, CancellationToken cancellationToken);

    /// <summary>Calls <c>DeleteZone</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<DeleteZoneResponse> DeleteZoneAsync(DeleteZoneRequest request, CancellationToken cancellationToken);

    /// <summary>Calls <c>GetZone</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<GetZoneResponse> GetZoneAsync(GetZoneRequest request, CancellationToken cancellationToken);

    /// <summary>Calls <c>ListZones</c>.</summary>
    /// <param name="request">The wire request.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    Task<ListZonesResponse> ListZonesAsync(ListZonesRequest request, CancellationToken cancellationToken);
}
