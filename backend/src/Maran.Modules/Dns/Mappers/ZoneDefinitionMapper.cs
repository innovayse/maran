using Maran.Agent.Client.Services.DnsService;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Dns.Mappers;

/// <summary>
/// Restates a zone and the settings in force as the agent's vocabulary: the complete description the daemon
/// is handed, whose serial is the zone's own. Translates; never decides.
/// </summary>
/// <remarks>
/// A zone that is signed or being withdrawn is described WITH its decrypted key, in every publication, whoever
/// asks for it: an edit and the start-up reconciler alike. Leaving the key out of one of them would publish the
/// zone unsigned while the registrar still holds its DS record.
/// </remarks>
public static class ZoneDefinitionMapper
{
    /// <summary>Builds the agent's description of a zone.</summary>
    /// <param name="zone">The zone, with its records and its signing keys loaded.</param>
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
            records,
            SigningOf(zone));
    }

    /// <summary>Restates the zone's DNSSEC step and key as the agent's signing instruction.</summary>
    /// <param name="zone">The zone, with its signing keys loaded.</param>
    /// <returns>The instruction, or null for a zone that is off.</returns>
    /// <exception cref="InvalidOperationException">A zone that is signed holds no key, which a missing include would cause.</exception>
    private static DnsSigningDefinition? SigningOf(Zone zone)
    {
        if (zone.DnssecState == DnssecState.Off)
        {
            return null;
        }

        var key = zone.SigningKeys.SingleOrDefault()
            ?? throw new InvalidOperationException("A signed zone is described with its key loaded.");

        return new DnsSigningDefinition(
            zone.DnssecState == DnssecState.Signing ? DnsSigningPhase.Active : DnsSigningPhase.Withdrawing,
            new DnsSigningKeyDefinition(key.PublicKey, new SensitiveString(key.PrivateKey), key.Flags, key.KeyTag));
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
