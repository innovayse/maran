using Maran.Agent.Client.Interfaces;
using Maran.Agent.V1;

namespace Maran.Agent.Client.Tests.Services.DnsService;

/// <summary>Stub of <c>IDnsServiceInvoker</c> returning canned responses and keeping every request.</summary>
/// <remarks>
/// A response left at its default carries neither branch of the oneof, which is how the tests ask for the
/// refusal of an empty answer. Every captured request is read by a test.
/// </remarks>
internal sealed class StubDnsService : IDnsServiceInvoker
{
    /// <summary>Response returned from <see cref="EnableDnsAsync"/>.</summary>
    public EnableDnsResponse EnableDnsResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="DisableDnsAsync"/>.</summary>
    public DisableDnsResponse DisableDnsResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="GetDnsStatusAsync"/>.</summary>
    public GetDnsStatusResponse GetDnsStatusResponse { get; set; } = new();

    /// <summary>Response returned from <see cref="PutZoneAsync"/>.</summary>
    public PutZoneResponse PutZoneResponse { get; set; } = new();

    /// <summary>The last <c>PutZone</c> request the stub received, for asserting the mapping.</summary>
    public PutZoneRequest? LastPutZoneRequest { get; private set; }

    /// <summary>Response returned from <see cref="DeleteZoneAsync"/>.</summary>
    public DeleteZoneResponse DeleteZoneResponse { get; set; } = new();

    /// <summary>The last <c>DeleteZone</c> request the stub received, for asserting the mapping.</summary>
    public DeleteZoneRequest? LastDeleteZoneRequest { get; private set; }

    /// <summary>Response returned from <see cref="GetZoneAsync"/>.</summary>
    public GetZoneResponse GetZoneResponse { get; set; } = new();

    /// <summary>The last <c>GetZone</c> request the stub received, for asserting the mapping.</summary>
    public GetZoneRequest? LastGetZoneRequest { get; private set; }

    /// <summary>Response returned from <see cref="ListZonesAsync"/>.</summary>
    public ListZonesResponse ListZonesResponse { get; set; } = new();

    /// <inheritdoc/>
    public Task<EnableDnsResponse> EnableDnsAsync(EnableDnsRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(EnableDnsResponse);
    }

    /// <inheritdoc/>
    public Task<DisableDnsResponse> DisableDnsAsync(DisableDnsRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(DisableDnsResponse);
    }

    /// <inheritdoc/>
    public Task<GetDnsStatusResponse> GetDnsStatusAsync(
        GetDnsStatusRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(GetDnsStatusResponse);
    }

    /// <inheritdoc/>
    public Task<PutZoneResponse> PutZoneAsync(PutZoneRequest request, CancellationToken cancellationToken)
    {
        LastPutZoneRequest = request;

        return Task.FromResult(PutZoneResponse);
    }

    /// <inheritdoc/>
    public Task<DeleteZoneResponse> DeleteZoneAsync(DeleteZoneRequest request, CancellationToken cancellationToken)
    {
        LastDeleteZoneRequest = request;

        return Task.FromResult(DeleteZoneResponse);
    }

    /// <inheritdoc/>
    public Task<GetZoneResponse> GetZoneAsync(GetZoneRequest request, CancellationToken cancellationToken)
    {
        LastGetZoneRequest = request;

        return Task.FromResult(GetZoneResponse);
    }

    /// <inheritdoc/>
    public Task<ListZonesResponse> ListZonesAsync(ListZonesRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(ListZonesResponse);
    }
}
