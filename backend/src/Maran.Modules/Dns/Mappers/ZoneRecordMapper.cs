using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Dns.Mappers;

/// <summary>Restates a <see cref="ZoneRecord"/> as the wire shape a screen reads. Translates; never decides.</summary>
public static class ZoneRecordMapper
{
    /// <summary>Maps one record.</summary>
    /// <param name="record">The record row.</param>
    /// <param name="names">The viewer's-language lookup of the module's display names.</param>
    /// <returns>The wire shape.</returns>
    public static ZoneRecordDto From(ZoneRecord record, IStringLocalizer<DisplayNames> names)
    {
        return new ZoneRecordDto(
            record.Id,
            record.ZoneId,
            record.Name,
            record.Type,
            record.Ttl,
            record.Value,
            record.Priority,
            record.Weight,
            record.Port,
            record.Flags,
            record.Tag,
            record.Origin,
            names[$"DnsOrigin{record.Origin}"].Value,
            record.Origin == RecordOrigin.Customer,
            record.CreatedAt);
    }
}
