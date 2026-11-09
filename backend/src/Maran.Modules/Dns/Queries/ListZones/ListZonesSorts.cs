using Maran.Modules.Dns.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Dns.Queries.ListZones;

/// <summary>
/// The orders the zones list accepts. <see cref="ListZonesQueryValidator"/> refuses any other sort and
/// <see cref="ListZonesQueryHandler"/> orders by exactly these, so the whitelist is written once.
/// </summary>
public static class ListZonesSorts
{
    /// <summary>
    /// <see cref="ZoneSortFields.Name"/> and <see cref="ZoneSortFields.CreatedAt"/>, by name when none is
    /// named, with ties broken by the row id.
    /// </summary>
    public static readonly SortMap<Zone> Map = new SortMapBuilder<Zone>()
        .By(ZoneSortFields.Name, zone => zone.Name)
        .By(ZoneSortFields.CreatedAt, zone => zone.CreatedAt)
        .DefaultTo(ZoneSortFields.Name, SortDirection.Ascending)
        .TieBreaker(zone => zone.Id);
}
