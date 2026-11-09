using Maran.Modules.Databases.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Databases.Queries.ListDatabaseUsers;

/// <summary>The sort whitelist of the database users list: the fields it exposes, its default and its tie-breaker.</summary>
public static class ListDatabaseUsersSorts
{
    /// <summary>The sort map; oldest first by default, ties broken by the row identity.</summary>
    public static readonly SortMap<DatabaseUser> Map = new SortMapBuilder<DatabaseUser>()
        .By(DatabaseUserSortFields.Name, user => user.FullName)
        .By(DatabaseUserSortFields.CreatedAt, user => user.CreatedAt)
        .DefaultTo(DatabaseUserSortFields.CreatedAt, SortDirection.Ascending)
        .TieBreaker(user => user.Id);
}
