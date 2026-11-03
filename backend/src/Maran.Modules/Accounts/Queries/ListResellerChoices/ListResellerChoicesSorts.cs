using Maran.Modules.Accounts.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Accounts.Queries.ListResellerChoices;

/// <summary>The orders the reseller picker accepts, read by its validator and its handler alike.</summary>
public static class ListResellerChoicesSorts
{
    /// <summary>
    /// <see cref="ResellerChoiceSortFields.Name"/> only, alphabetical by default — a picker is scanned
    /// by name — ties broken by id.
    /// </summary>
    public static readonly SortMap<Reseller> Map = new SortMapBuilder<Reseller>()
        .By(ResellerChoiceSortFields.Name, reseller => reseller.Name)
        .DefaultTo(ResellerChoiceSortFields.Name, SortDirection.Ascending)
        .TieBreaker(reseller => reseller.Id);
}
