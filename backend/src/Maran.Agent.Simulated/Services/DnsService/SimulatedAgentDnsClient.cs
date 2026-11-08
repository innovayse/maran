using System.Collections.Concurrent;
using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DnsService;

namespace Maran.Agent.Simulated.Services.DnsService;

/// <summary>
/// The agent's DNS operations against the simulated server's memory: no daemon is installed, no zone
/// file is written and nothing listens on port 53.
/// </summary>
/// <remarks>
/// The status answers from what was asked of it, so it agrees with the panel by construction, which is
/// the one property the real status refuses to have. Zones are kept by apex name with the serial and the
/// record count the panel sent, which is all the panel ever reads back.
/// </remarks>
public sealed class SimulatedAgentDnsClient : IAgentDnsClient
{
    /// <summary>The zones this simulated server serves, by apex name.</summary>
    private readonly ConcurrentDictionary<string, DnsZoneServedDto> _zones = new(StringComparer.Ordinal);

    /// <summary>Whether the simulated daemon is switched on; written and read under <see cref="_gate"/>.</summary>
    private bool _running;

    /// <summary>Guards <see cref="_running"/>.</summary>
    private readonly object _gate = new();

    /// <inheritdoc/>
    public Task<Result<DnsDaemonStatusDto>> EnableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _running = true;
        }

        return Task.FromResult(Result<DnsDaemonStatusDto>.Ok(Status()));
    }

    /// <inheritdoc/>
    public Task<Result<DnsDaemonStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _running = false;
        }

        return Task.FromResult(Result<DnsDaemonStatusDto>.Ok(Status()));
    }

    /// <inheritdoc/>
    public Task<Result<DnsDaemonStatusDto>> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Result<DnsDaemonStatusDto>.Ok(Status()));
    }

    /// <inheritdoc/>
    public Task<Result<DnsZoneServedDto>> PutZoneAsync(DnsZoneDefinition zone, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var served = new DnsZoneServedDto(zone.Name, zone.Serial);
        _zones[zone.Name] = served;

        return Task.FromResult(Result<DnsZoneServedDto>.Ok(served));
    }

    /// <inheritdoc/>
    public Task<Result> DeleteZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var removed = _zones.TryRemove(zoneName, out _);

        return Task.FromResult(removed ? Result.Ok() : Result.Fail(NotServed()));
    }

    /// <inheritdoc/>
    public Task<Result<DnsZoneServedDto>> GetZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(_zones.TryGetValue(zoneName, out var served)
            ? Result<DnsZoneServedDto>.Ok(served)
            : Result<DnsZoneServedDto>.Fail(NotServed()));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<string>>> ListZonesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<string> all = _zones.Keys.Order(StringComparer.Ordinal).ToList();

        return Task.FromResult(Result<IReadOnlyList<string>>.Ok(all));
    }

    /// <summary>The daemon state the simulated server reports.</summary>
    /// <returns>Running and listening together, because here nothing can differ.</returns>
    private DnsDaemonStatusDto Status()
    {
        lock (_gate)
        {
            return new DnsDaemonStatusDto(_running, _running, _running, DnsPolicyState.NotObserved, DnsPolicyState.NotObserved);
        }
    }

    /// <summary>The failure for a zone the simulated daemon does not serve.</summary>
    /// <returns>The agent's own not-found code, so the panel exercises its real translation.</returns>
    private static Error NotServed()
    {
        return Error.Of("AgentNotFound", ErrorType.NotFound);
    }
}
