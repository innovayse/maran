using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.DnsService;
using Maran.Host.Tests.Fixtures;
using Maran.SharedKernel.Results;

namespace Maran.Host.Tests.Resilience;

/// <summary>An inner DNS client that records its arguments and can fail or hang on demand.</summary>
/// <remarks>
/// Hanging is the point: only a call that never returns proves the decorator applies a TIMEOUT rather
/// than merely forwarding.
/// </remarks>
internal sealed class RecordingAgentDnsClient : IAgentDnsClient
{
    /// <summary>The status every status-returning call answers.</summary>
    private static readonly DnsDaemonStatusDto Status =
        new(true, true, true, DnsPolicyState.InForce, DnsPolicyState.NotInForce);

    /// <summary>How many calls fail with a transport error before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>When true, every call waits for its cancellation token instead of returning.</summary>
    public bool Hangs { get; set; }

    /// <summary>How many times any method on this client was entered.</summary>
    public int Calls { get; private set; }

    /// <summary>The zone of the last <see cref="PutZoneAsync"/> call.</summary>
    public DnsZoneDefinition? LastZone { get; private set; }

    /// <summary>The zone name of the last delete, get or put call.</summary>
    public string? LastZoneName { get; private set; }

    /// <inheritdoc/>
    public async Task<Result<DnsDaemonStatusDto>> EnableAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<DnsDaemonStatusDto>.Ok(Status);
    }

    /// <inheritdoc/>
    public async Task<Result<DnsDaemonStatusDto>> DisableAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<DnsDaemonStatusDto>.Ok(Status);
    }

    /// <inheritdoc/>
    public async Task<Result<DnsDaemonStatusDto>> GetStatusAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        return Result<DnsDaemonStatusDto>.Ok(Status);
    }

    /// <inheritdoc/>
    public async Task<Result<DnsZoneServedDto>> PutZoneAsync(DnsZoneDefinition zone, CancellationToken cancellationToken)
    {
        LastZone = zone;
        LastZoneName = zone.Name;

        await EnterAsync(cancellationToken);

        return Result<DnsZoneServedDto>.Ok(new DnsZoneServedDto(zone.Name, zone.Serial));
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        LastZoneName = zoneName;

        await EnterAsync(cancellationToken);

        return Result.Ok();
    }

    /// <inheritdoc/>
    public async Task<Result<DnsZoneServedDto>> GetZoneAsync(string zoneName, CancellationToken cancellationToken)
    {
        LastZoneName = zoneName;

        await EnterAsync(cancellationToken);

        return Result<DnsZoneServedDto>.Ok(new DnsZoneServedDto(zoneName, 1));
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<string>>> ListZonesAsync(CancellationToken cancellationToken)
    {
        await EnterAsync(cancellationToken);

        IReadOnlyList<string> zones = ["example.com"];

        return Result<IReadOnlyList<string>>.Ok(zones);
    }

    /// <summary>Counts the call and applies whichever misbehaviour the test asked for.</summary>
    /// <param name="cancellationToken">The token the pipeline's timeout cancels.</param>
    /// <returns>A task that completes once the call may return.</returns>
    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        Calls++;

        if (Calls <= FailuresBeforeSuccess)
        {
            throw AgentTransportFaults.ConnectRefused();
        }

        if (Hangs)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        await Task.Yield();
    }
}
