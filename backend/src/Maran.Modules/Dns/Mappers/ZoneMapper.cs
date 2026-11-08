using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;

namespace Maran.Modules.Dns.Mappers;

/// <summary>Restates a <see cref="Zone"/> as the wire shape a screen reads. Translates; never decides.</summary>
public static class ZoneMapper
{
    /// <summary>Maps one zone.</summary>
    /// <param name="zone">The zone row.</param>
    /// <param name="recordCount">How many records the zone holds.</param>
    /// <param name="ownerNames">Account names by id, from the account directory; a missing owner reads as empty.</param>
    /// <param name="nameservers">The name servers the zone names, from the settings.</param>
    /// <returns>The wire shape.</returns>
    public static ZoneDto From(
        Zone zone,
        int recordCount,
        IReadOnlyDictionary<Guid, string> ownerNames,
        IReadOnlyList<string> nameservers)
    {
        return new ZoneDto(
            zone.Id,
            zone.AccountId,
            ownerNames.GetValueOrDefault(zone.AccountId, string.Empty),
            zone.Name,
            zone.Serial,
            recordCount,
            zone.CreatedAt,
            zone.UpdatedAt,
            nameservers);
    }
}
