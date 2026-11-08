using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;

namespace Maran.Modules.Dns.Mappers;

/// <summary>
/// Restates a zone and the settings in force as the agent's vocabulary: the complete description the daemon
/// is handed, whose serial is the zone's own. Translates; never decides.
/// </summary>
public static class ZoneDefinitionMapper
{
    /// <summary>Builds the agent's description of a zone.</summary>
    /// <param name="zone">The zone, with its records loaded.</param>
    /// <param name="settings">The settings: name servers, SOA mailbox and default time to live.</param>
    /// <returns>The description the agent publishes.</returns>
    public static DnsZoneDefinition From(Zone zone, DnsSettings settings)
    {
        var records = zone.Records
            .OrderBy(record =>
            {
                return record.Name;
            }, StringComparer.Ordinal)
            .ThenBy(record =>
            {
                return record.Type;
            })
            .ThenBy(record =>
            {
                return record.Value;
            }, StringComparer.Ordinal)
            .Select(record =>
            {
                return new DnsRecordDefinition(
                    record.Name,
                    KindOf(record.Type),
                    record.Ttl,
                    record.Value,
                    record.Priority,
                    record.Weight,
                    record.Port,
                    record.Flags,
                    record.Tag);
            })
            .ToList();

        return new DnsZoneDefinition(
            zone.Name,
            zone.Serial,
            settings.PrimaryNameserver,
            settings.HostmasterMailbox,
            settings.Nameservers,
            records);
    }

    /// <summary>Maps the panel's record type to the agent's.</summary>
    /// <param name="type">The panel's type.</param>
    /// <returns>The agent's kind.</returns>
    private static DnsRecordKind KindOf(RecordType type)
    {
        return type switch
        {
            RecordType.A => DnsRecordKind.A,
            RecordType.Aaaa => DnsRecordKind.Aaaa,
            RecordType.Cname => DnsRecordKind.Cname,
            RecordType.Mx => DnsRecordKind.Mx,
            RecordType.Txt => DnsRecordKind.Txt,
            RecordType.Srv => DnsRecordKind.Srv,
            RecordType.Caa => DnsRecordKind.Caa,
            RecordType.Ns => DnsRecordKind.Ns,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown record type."),
        };
    }
}
