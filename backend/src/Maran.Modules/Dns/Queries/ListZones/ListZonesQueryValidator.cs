using Maran.Modules.Dns.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Dns.Queries.ListZones;

/// <summary>Holds <see cref="ListZonesQuery"/> to the panel's paging bounds (rules/security.md "Input").</summary>
/// <remarks>
/// The list's only typed filter is an account id, which the binder either parses as a GUID or refuses, so
/// there is nothing beyond the paging contract for this validator to add.
/// </remarks>
public sealed class ListZonesQueryValidator : PagedQueryValidator<ListZonesQuery, Zone>
{
    /// <summary>Declares the paging rules against <see cref="ListZonesSorts.Map"/>.</summary>
    public ListZonesQueryValidator()
        : base(ListZonesSorts.Map)
    {
    }
}
