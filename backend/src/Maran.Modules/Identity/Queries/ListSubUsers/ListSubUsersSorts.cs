using Maran.Modules.Identity.Models;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Identity.Queries.ListSubUsers;

/// <summary>The sub-users list's sort whitelist, default and tie-breaker.</summary>
public static class ListSubUsersSorts
{
    /// <summary>The sort map the validator checks a client's sort against and the pager applies.</summary>
    public static readonly SortMap<SubUserRow> Map = new SortMapBuilder<SubUserRow>()
        .By(SubUserSortFields.Username, row => row.Username)
        .By(SubUserSortFields.Email, row => row.Email)
        .By(SubUserSortFields.CreatedAt, row => row.CreatedAt)
        .DefaultTo(SubUserSortFields.CreatedAt, SortDirection.Ascending)
        .TieBreaker(row => row.Id);
}
