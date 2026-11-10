using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DnsService;
using Maran.SharedKernel.Results;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// An <see cref="IAgentDnsClient"/> that accepts every request, so an HTTP test exercises the panel's
/// authorisation and tenancy without a root agent and a name server.
/// </summary>
public sealed class StubAgentDnsClient : IAgentDnsClient
{
    /// <inheritdoc />
    public Task<Result<DnsDaemonStatusDto>> EnableAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<DnsDaemonStatusDto>.Ok(new DnsDaemonStatusDto(true, true, true, DnsPolicyState.InForce, DnsPolicyState.InForce)));
    }

    /// <inheritdoc />
    public Task<Result<DnsDaemonStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<DnsDaemonStatusDto>.Ok(new DnsDaemonStatusDto(false, false, false, DnsPolicyState.NotObserved, DnsPolicyState.NotObserved)));
    }

    /// <inheritdoc />
    public Task<Result<DnsDaemonStatusDto>> GetStatusAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<DnsDaemonStatusDto>.Ok(new DnsDaemonStatusDto(
            true, true, true, DnsPolicyState.InForce, DnsPolicyState.InForce, DnssecSupported: true)));
    }

    /// <inheritdoc />
    public Task<Result<DnsZoneServedDto>> PutZoneAsync(DnsZoneDefinition zone, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<DnsZoneServedDto>.Ok(new DnsZoneServedDto(zone.Name, zone.Serial)));
    }

    /// <inheritdoc />
    public Task<Result> DeleteZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result.Ok());
    }

    /// <inheritdoc />
    public Task<Result<DnsZoneServedDto>> GetZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        return Task.FromResult(Result<DnsZoneServedDto>.Ok(new DnsZoneServedDto(zoneName, 0)));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<string>>> ListZonesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> none = [];

        return Task.FromResult(Result<IReadOnlyList<string>>.Ok(none));
    }
}
