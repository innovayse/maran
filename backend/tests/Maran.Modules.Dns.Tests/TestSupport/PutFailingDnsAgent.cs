using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DnsService;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Dns.Tests.TestSupport;

/// <summary>
/// An <see cref="IAgentDnsClient"/> that behaves as the recording agent does except that publishing ONE
/// named zone is refused, so a test can show that a pass over many zones goes on after a failure.
/// </summary>
public sealed class PutFailingDnsAgent : IAgentDnsClient
{
    /// <summary>The agent every call is passed to.</summary>
    private readonly RecordingDnsAgent _inner;

    /// <summary>The apex whose publication is refused.</summary>
    private readonly string _refusedZone;

    /// <summary>Creates the decorator.</summary>
    /// <param name="inner">The agent every other call is passed to.</param>
    /// <param name="refusedZone">The apex whose publication fails.</param>
    public PutFailingDnsAgent(RecordingDnsAgent inner, string refusedZone)
    {
        _inner = inner;
        _refusedZone = refusedZone;
    }

    /// <inheritdoc />
    public Task<Result<DnsDaemonStatusDto>> EnableAsync(CancellationToken cancellationToken)
    {
        return _inner.EnableAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<DnsDaemonStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        return _inner.DisableAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<DnsDaemonStatusDto>> GetStatusAsync(CancellationToken cancellationToken)
    {
        return _inner.GetStatusAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<DnsZoneServedDto>> PutZoneAsync(DnsZoneDefinition zone, CancellationToken cancellationToken)
    {
        if (string.Equals(zone.Name, _refusedZone, StringComparison.Ordinal))
        {
            return Task.FromResult(Result<DnsZoneServedDto>.Fail(Error.Of("AgentSystemFailure", ErrorType.Failure)));
        }

        return _inner.PutZoneAsync(zone, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result> DeleteZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        return _inner.DeleteZoneAsync(zoneName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<DnsZoneServedDto>> GetZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        return _inner.GetZoneAsync(zoneName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<string>>> ListZonesAsync(CancellationToken cancellationToken)
    {
        return _inner.ListZonesAsync(cancellationToken);
    }
}
