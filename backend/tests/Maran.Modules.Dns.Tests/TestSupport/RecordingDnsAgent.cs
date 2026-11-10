using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DnsService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentDnsClient"/> that serves zones from memory, records every call, and can be told to
/// refuse the next one, so a test states what the panel asked of the host and how the host answered.
/// </summary>
public sealed class RecordingDnsAgent : IAgentDnsClient
{
    /// <summary>The zones the stand-in serves, by apex name.</summary>
    private readonly Dictionary<string, DnsZoneServedDto> _served = new(StringComparer.Ordinal);

    /// <summary>Every zone description the panel sent, in order.</summary>
    public List<DnsZoneDefinition> Puts { get; } = [];

    /// <summary>Every zone name the panel asked to remove, in order.</summary>
    public List<string> Deletes { get; } = [];

    /// <summary>How many times the daemon was switched on.</summary>
    public int EnableCalls { get; private set; }

    /// <summary>How many times the daemon was switched off.</summary>
    public int DisableCalls { get; private set; }

    /// <summary>Whether the daemon reports that it can sign zones. A test turns it off to describe an older build.</summary>
    public bool DnssecSupported { get; set; } = true;

    /// <summary>What the daemon reports about the signing of a zone it serves; null for an answer with no observation.</summary>
    public DnsSigningObservedDto? Observation { get; set; }

    /// <summary>When set, the next call of any kind fails with it, and the field clears.</summary>
    public Error? FailNext { get; set; }

    /// <summary>The apex names currently served.</summary>
    public IReadOnlyCollection<string> ServedNames
    {
        get
        {
            return _served.Keys;
        }
    }

    /// <inheritdoc />
    public Task<Result<DnsDaemonStatusDto>> EnableAsync(CancellationToken cancellationToken)
    {
        if (TakeFailure() is { } error)
        {
            return Task.FromResult(Result<DnsDaemonStatusDto>.Fail(error));
        }

        EnableCalls++;
        return Task.FromResult(Result<DnsDaemonStatusDto>.Ok(Running()));
    }

    /// <inheritdoc />
    public Task<Result<DnsDaemonStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        if (TakeFailure() is { } error)
        {
            return Task.FromResult(Result<DnsDaemonStatusDto>.Fail(error));
        }

        DisableCalls++;
        return Task.FromResult(Result<DnsDaemonStatusDto>.Ok(Stopped()));
    }

    /// <inheritdoc />
    public Task<Result<DnsDaemonStatusDto>> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (TakeFailure() is { } error)
        {
            return Task.FromResult(Result<DnsDaemonStatusDto>.Fail(error));
        }

        return Task.FromResult(Result<DnsDaemonStatusDto>.Ok(Running()));
    }

    /// <inheritdoc />
    public Task<Result<DnsZoneServedDto>> PutZoneAsync(DnsZoneDefinition zone, CancellationToken cancellationToken)
    {
        if (TakeFailure() is { } error)
        {
            return Task.FromResult(Result<DnsZoneServedDto>.Fail(error));
        }

        Puts.Add(zone);
        var served = new DnsZoneServedDto(zone.Name, zone.Serial);
        _served[zone.Name] = served;

        return Task.FromResult(Result<DnsZoneServedDto>.Ok(served));
    }

    /// <inheritdoc />
    public Task<Result> DeleteZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        if (TakeFailure() is { } error)
        {
            return Task.FromResult(Result.Fail(error));
        }

        Deletes.Add(zoneName);
        var removed = _served.Remove(zoneName);

        return Task.FromResult(
            removed ? Result.Ok() : Result.Fail(Error.Of("AgentNotFound", ErrorType.NotFound)));
    }

    /// <inheritdoc />
    public Task<Result<DnsZoneServedDto>> GetZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        if (TakeFailure() is { } error)
        {
            return Task.FromResult(Result<DnsZoneServedDto>.Fail(error));
        }

        return Task.FromResult(_served.TryGetValue(zoneName, out var served)
            ? Result<DnsZoneServedDto>.Ok(served with { Signing = Observation })
            : Result<DnsZoneServedDto>.Fail(Error.Of("AgentNotFound", ErrorType.NotFound)));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<string>>> ListZonesAsync(CancellationToken cancellationToken)
    {
        if (TakeFailure() is { } error)
        {
            return Task.FromResult(Result<IReadOnlyList<string>>.Fail(error));
        }

        IReadOnlyList<string> all = _served.Keys.ToList();

        return Task.FromResult(Result<IReadOnlyList<string>>.Ok(all));
    }

    /// <summary>The state of a daemon that is up.</summary>
    /// <returns>Running and answering on port 53.</returns>
    private DnsDaemonStatusDto Running()
    {
        return new DnsDaemonStatusDto(
            true, true, true, DnsPolicyState.InForce, DnsPolicyState.InForce, DnssecSupported);
    }

    /// <summary>The state of a daemon that is down.</summary>
    /// <returns>Stopped and not answering.</returns>
    private static DnsDaemonStatusDto Stopped()
    {
        return new DnsDaemonStatusDto(false, false, false, DnsPolicyState.NotObserved, DnsPolicyState.NotObserved);
    }

    /// <summary>Takes the armed failure, if any, so it applies to exactly one call.</summary>
    /// <returns>The failure, or null when none is armed.</returns>
    private Error? TakeFailure()
    {
        var error = FailNext;
        FailNext = null;

        return error;
    }
}
