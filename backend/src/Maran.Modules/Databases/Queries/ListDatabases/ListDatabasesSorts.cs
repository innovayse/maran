using Maran.Modules.Databases.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Databases.Queries.ListDatabases;

/// <summary>
/// The orders the databases list accepts. <see cref="ListDatabasesQueryValidator"/> refuses any other
/// sort and <see cref="ListDatabasesQueryHandler"/> orders by exactly these, so the whitelist is
/// written once.
/// </summary>
public static class ListDatabasesSorts
{
    /// <summary>
    /// <see cref="DatabaseSortFields.Name"/> and <see cref="DatabaseSortFields.CreatedAt"/>, oldest
    /// first when none is named — the order the list had before it was paged — with ties broken by the
    /// database id.
    /// </summary>
    /// <remarks>
    /// The name orders by <see cref="Database.FullName"/>, not by the customer's unprefixed
    /// <see cref="Database.Name"/>: the full name is the one the list shows and the one a search
    /// matches, so a list sorted by name reads in order down the column the reader sees. Across one
    /// account the two orders agree, because every full name there carries the same prefix.
    /// </remarks>
    public static readonly SortMap<Database> Map = new SortMapBuilder<Database>()
        .By(DatabaseSortFields.Name, database => database.FullName)
        .By(DatabaseSortFields.CreatedAt, database => database.CreatedAt)
        .DefaultTo(DatabaseSortFields.CreatedAt, SortDirection.Ascending)
        .TieBreaker(database => database.Id);
}
