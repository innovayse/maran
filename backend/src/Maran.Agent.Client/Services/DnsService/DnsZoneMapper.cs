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
            ToState(status.ResponseRateLimiting));
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
