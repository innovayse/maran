using Maran.Modules.Dns.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Dns.Queries.ListRecords;

/// <summary>
/// The orders the records list accepts. <see cref="ListRecordsQueryValidator"/> refuses any other sort and
/// <see cref="ListRecordsQueryHandler"/> orders by exactly these, so the whitelist is written once.
/// </summary>
public static class ListRecordsSorts
{
    /// <summary>
    /// <see cref="ZoneRecordSortFields.Name"/>, <see cref="ZoneRecordSortFields.Type"/> and
    /// <see cref="ZoneRecordSortFields.Ttl"/>, by name when none is named, with ties broken by the row id.
    /// </summary>
    public static readonly SortMap<ZoneRecord> Map = new SortMapBuilder<ZoneRecord>()
        .By(ZoneRecordSortFields.Name, record => record.Name)
        .By(ZoneRecordSortFields.Type, record => record.Type)
        .By(ZoneRecordSortFields.Ttl, record => record.Ttl)
        .DefaultTo(ZoneRecordSortFields.Name, SortDirection.Ascending)
        .TieBreaker(record => record.Id);
}
