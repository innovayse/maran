using Maran.Modules.Dns.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Dns.Queries.ListZoneTemplates;

/// <summary>
/// The orders the templates list accepts. <see cref="ListZoneTemplatesQueryValidator"/> refuses any other sort and
/// <see cref="ListZoneTemplatesQueryHandler"/> orders by exactly these, so the whitelist is written once.
/// </summary>
public static class ListZoneTemplatesSorts
{
    /// <summary>
    /// <see cref="ZoneTemplateSortFields.Name"/> and <see cref="ZoneTemplateSortFields.UpdatedAt"/>, by name when
    /// none is named, with ties broken by the row id.
    /// </summary>
    public static readonly SortMap<ZoneTemplate> Map = new SortMapBuilder<ZoneTemplate>()
        .By(ZoneTemplateSortFields.Name, template => template.Name)
        .By(ZoneTemplateSortFields.UpdatedAt, template => template.UpdatedAt)
        .DefaultTo(ZoneTemplateSortFields.Name, SortDirection.Ascending)
        .TieBreaker(template => template.Id);
}
