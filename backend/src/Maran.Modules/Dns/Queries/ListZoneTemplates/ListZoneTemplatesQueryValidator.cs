using Maran.Modules.Dns.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Dns.Queries.ListZoneTemplates;

/// <summary>Holds <see cref="ListZoneTemplatesQuery"/> to the panel's paging bounds (rules/security.md "Input").</summary>
public sealed class ListZoneTemplatesQueryValidator : PagedQueryValidator<ListZoneTemplatesQuery, ZoneTemplate>
{
    /// <summary>Declares the paging rules against <see cref="ListZoneTemplatesSorts.Map"/>.</summary>
    public ListZoneTemplatesQueryValidator()
        : base(ListZoneTemplatesSorts.Map)
    {
    }
}
