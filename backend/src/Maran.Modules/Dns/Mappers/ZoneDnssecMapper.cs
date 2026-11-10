using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;

namespace Maran.Modules.Dns.Mappers;

/// <summary>
/// Restates a zone's DNSSEC step and key as the wire shape a screen reads. The private key is not read here:
/// nothing in this mapper has a way to put it on the wire.
/// </summary>
public static class ZoneDnssecMapper
{
    /// <summary>Maps one zone's situation.</summary>
    /// <param name="zone">The zone, with its signing keys loaded.</param>
    /// <param name="available">Whether the host was seen to be able to sign.</param>
    /// <param name="observed">What the name server was seen to do, or null when it was not asked or could not be.</param>
    /// <returns>The wire shape.</returns>
    public static ZoneDnssecDto From(Zone zone, bool available, DnsSigningObservedDto? observed)
    {
        var key = zone.SigningKeys.SingleOrDefault();
        var ds = key is null ? null : DsRecordPolicy.For(zone.Name, key);
        var started = zone.DnssecWithdrawalStartedAt;

        return new ZoneDnssecDto(
            zone.DnssecState,
            available,
            ds is null ? null : new DsRecordDto(ds.KeyTag, ds.Algorithm, ds.DigestType, ds.Digest),
            key is null ? null : new DnskeyRecordDto(key.Flags, DnssecKeyPolicy.Protocol, key.Algorithm, key.PublicKey),
            observed is null ? null : Observation(observed),
            started,
            started is { } at ? DnssecWithdrawalPolicy.EarliestFinish(at) : null,
            RolloverAutomatic: false);
    }

    /// <summary>Maps what the daemon was seen to do.</summary>
    /// <param name="observed">The agent client's observation.</param>
    /// <returns>The wire shape.</returns>
    private static ZoneDnssecObservationDto Observation(DnsSigningObservedDto observed)
    {
        return new ZoneDnssecObservationDto(
            StateOf(observed.Phase),
            observed.PhaseObserved,
            observed.PublishedKeyTag,
            observed.PublishedKeyObserved);
    }

    /// <summary>Maps the daemon's phase to the panel's step.</summary>
    /// <param name="phase">The observed phase.</param>
    /// <returns>The matching step.</returns>
    private static DnssecState StateOf(DnsSigningPhase phase)
    {
        return phase switch
        {
            DnsSigningPhase.Off => DnssecState.Off,
            DnsSigningPhase.Active => DnssecState.Signing,
            DnsSigningPhase.Withdrawing => DnssecState.Withdrawing,
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown DNS signing phase."),
        };
    }
}
