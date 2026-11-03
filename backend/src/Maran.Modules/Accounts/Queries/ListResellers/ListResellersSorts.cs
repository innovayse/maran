using Maran.Modules.Accounts.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Queries.ListResellers;

/// <summary>
/// The orders the resellers list accepts. <see cref="ListResellersQueryValidator"/> refuses any other
/// sort and <see cref="ListResellersQueryHandler"/> orders by exactly these, so the whitelist is
/// written once. Usage figures are not sortable: they are measured for the page after it is chosen.
/// </summary>
public static class ListResellersSorts
{
    /// <summary>
    /// <see cref="ResellerSortFields.Name"/>, <see cref="ResellerSortFields.CreatedAt"/> and
    /// <see cref="ResellerSortFields.Status"/>, alphabetical by name when none is named, with ties broken
    /// by the reseller id.
    /// </summary>
    /// <remarks>
    /// The status is stored as its member name, so PostgreSQL orders it alphabetically by that name,
    /// not by the enum's numeric value.
    /// </remarks>
    public static readonly SortMap<Reseller> Map = new SortMapBuilder<Reseller>()
        .By(ResellerSortFields.Name, reseller => reseller.Name)
        .By(ResellerSortFields.CreatedAt, reseller => reseller.CreatedAt)
        .By(ResellerSortFields.Status, reseller => reseller.Status)
        .DefaultTo(ResellerSortFields.Name, SortDirection.Ascending)
        .TieBreaker(reseller => reseller.Id);
}
