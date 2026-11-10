using Maran.Agent.V1;

namespace Maran.Agent.Client.Services.DnsService;

/// <summary>
/// Restates the panel's zone vocabulary on the wire and the wire's answers in the panel's. Translates and
/// never decides, with one exception it states: the SOA timers, which the panel does not let anyone tune.
/// </summary>
internal static class DnsZoneMapper
{
    /// <summary>The SOA refresh interval, in seconds, that every zone is published with.</summary>
    private const uint SoaRefresh = 7_200;

    /// <summary>The SOA retry interval, in seconds.</summary>
    private const uint SoaRetry = 3_600;

    /// <summary>The SOA expire limit, in seconds: two weeks.</summary>
    private const uint SoaExpire = 1_209_600;

    /// <summary>The SOA negative-caching time, in seconds.</summary>
    private const uint SoaNegativeTtl = 3_600;

    /// <summary>Builds the <c>PutZone</c> request for a zone.</summary>
    /// <param name="zone">The zone and its complete record set.</param>
    /// <returns>The wire request.</returns>
    public static PutZoneRequest ToRequest(DnsZoneDefinition zone)
    {
        var request = new PutZoneRequest
        {
            ZoneName = zone.Name,
            Soa = new DnsZoneSoa
            {
                PrimaryNameserver = zone.PrimaryNameserver,
                Hostmaster = zone.HostmasterMailbox,
                Serial = checked((uint)zone.Serial),
                Refresh = SoaRefresh,
                Retry = SoaRetry,
                Expire = SoaExpire,
                NegativeTtl = SoaNegativeTtl,
            },
        };
        request.Nameservers.AddRange(zone.Nameservers);
        foreach (var record in zone.Records)
        {
            request.Records.Add(ToWire(record));
        }

        if (zone.Signing is not null)
        {
            request.Signing = ToWire(zone.Signing);
        }

        return request;
    }

    /// <summary>Reads a daemon-state answer.</summary>
    /// <param name="status">The wire status.</param>
    /// <returns>The panel's view of the daemon.</returns>
    public static DnsDaemonStatusDto ToStatus(DnsDaemonStatus status)
    {
        return new DnsDaemonStatusDto(
            status.Running,
            status.Port53Answering,
            status.EnabledAtBoot,
            ToState(status.RecursionRefused),
            ToState(status.ResponseRateLimiting),
            status.DnssecSupported);
    }

    /// <summary>Reads a zone's signing observation.</summary>
    /// <param name="observation">The wire observation; null when the answer carried none.</param>
    /// <returns>
    /// The panel's view, or null when there is no observation. A value the panel does not know reads as not
    /// observed, never as a phase or a key.
    /// </returns>
    public static DnsSigningObservedDto? ToObserved(DnsSigningObservation? observation)
    {
        if (observation is null)
        {
            return null;
        }

        var phase = ToPhase(observation.Mode);
        var phaseObserved = observation.ModeObserved == DnsPolicyObservation.InForce && phase is not null
            ? DnsPolicyState.InForce
            : DnsPolicyState.NotObserved;
        var keyObserved = ToState(observation.PublishedKeyObserved);

        return new DnsSigningObservedDto(
            phaseObserved == DnsPolicyState.InForce ? phase!.Value : DnsSigningPhase.Off,
            phaseObserved,
            keyObserved == DnsPolicyState.InForce ? checked((int)observation.PublishedKeyTag) : 0,
            keyObserved);
    }

    /// <summary>Puts a zone's signing instruction on the wire.</summary>
    /// <param name="signing">The panel's instruction.</param>
    /// <returns>The wire message; the private key is revealed here, for the one purpose it exists for.</returns>
    private static DnsSigning ToWire(DnsSigningDefinition signing)
    {
        var wire = new DnsSigning { Mode = ToWire(signing.Phase) };
        if (signing.Key is not null)
        {
            wire.Key = new DnsSigningKey
            {
                PublicKey = signing.Key.PublicKey,
                PrivateKey = signing.Key.PrivateKey.Reveal(),
                Flags = checked((uint)signing.Key.Flags),
                KeyTag = checked((uint)signing.Key.KeyTag),
            };
        }

        return wire;
    }

    /// <summary>Maps the panel's phase to the wire's mode.</summary>
    /// <param name="phase">The panel's phase.</param>
    /// <returns>The wire mode.</returns>
    private static DnsSigningMode ToWire(DnsSigningPhase phase)
    {
        return phase switch
        {
            DnsSigningPhase.Off => DnsSigningMode.Unsigned,
            DnsSigningPhase.Active => DnsSigningMode.Signed,
            DnsSigningPhase.Withdrawing => DnsSigningMode.Withdrawing,
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown DNS signing phase."),
        };
    }

    /// <summary>Maps the wire's mode to the panel's phase.</summary>
    /// <param name="mode">The wire mode.</param>
    /// <returns>The phase, or null for a value the panel does not know.</returns>
    private static DnsSigningPhase? ToPhase(DnsSigningMode mode)
    {
        return mode switch
        {
            DnsSigningMode.Unsigned => DnsSigningPhase.Off,
            DnsSigningMode.Signed => DnsSigningPhase.Active,
            DnsSigningMode.Withdrawing => DnsSigningPhase.Withdrawing,
            _ => null,
        };
    }

    /// <summary>Puts one record on the wire: the value goes into the one field its type reads.</summary>
    /// <param name="record">The panel's record.</param>
    /// <returns>The wire record.</returns>
    private static DnsRecord ToWire(DnsRecordDefinition record)
    {
        var wire = new DnsRecord
        {
            Name = record.Name,
            Type = ToWire(record.Kind),
            Ttl = checked((uint)record.Ttl),
            Priority = checked((uint)record.Priority),
            Weight = checked((uint)record.Weight),
            Port = checked((uint)record.Port),
        };

        switch (record.Kind)
        {
            case DnsRecordKind.A:
            case DnsRecordKind.Aaaa:
                wire.Address = record.Value;
                break;
            case DnsRecordKind.Txt:
                wire.Text = record.Value;
                break;
            case DnsRecordKind.Caa:
                wire.CaaFlags = checked((uint)record.Flags);
                wire.CaaTag = record.Tag;
                wire.CaaValue = record.Value;
                break;
            default:
                wire.Target = record.Value;
                break;
        }

        return wire;
    }

    /// <summary>Maps the wire's observation to the panel's.</summary>
    /// <param name="observation">The wire value.</param>
    /// <returns>The panel's state; an unknown value reads as not observed, never as in force.</returns>
    private static DnsPolicyState ToState(DnsPolicyObservation observation)
    {
        return observation switch
        {
            DnsPolicyObservation.InForce => DnsPolicyState.InForce,
            DnsPolicyObservation.NotInForce => DnsPolicyState.NotInForce,
            _ => DnsPolicyState.NotObserved,
        };
    }

    /// <summary>Maps a record kind to its wire enum.</summary>
    /// <param name="kind">The panel's kind.</param>
    /// <returns>The wire type.</returns>
    private static DnsRecordType ToWire(DnsRecordKind kind)
    {
        return kind switch
        {
            DnsRecordKind.A => DnsRecordType.A,
            DnsRecordKind.Aaaa => DnsRecordType.Aaaa,
            DnsRecordKind.Cname => DnsRecordType.Cname,
            DnsRecordKind.Mx => DnsRecordType.Mx,
            DnsRecordKind.Txt => DnsRecordType.Txt,
            DnsRecordKind.Srv => DnsRecordType.Srv,
            DnsRecordKind.Caa => DnsRecordType.Caa,
            DnsRecordKind.Ns => DnsRecordType.Ns,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown DNS record kind."),
        };
    }
}
