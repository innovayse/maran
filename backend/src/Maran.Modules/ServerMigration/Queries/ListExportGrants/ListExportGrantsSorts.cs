using Maran.Modules.ServerMigration.Models;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Queries.ListExportGrants;

/// <summary>
/// The orders the pull grants list accepts. <see cref="ListExportGrantsQueryValidator"/> refuses any other sort and
/// <see cref="ListExportGrantsQueryHandler"/> orders by exactly these.
/// </summary>
public static class ListExportGrantsSorts
{
    /// <summary>
    /// <see cref="ExportGrantSortFields.AccountName"/>, <see cref="ExportGrantSortFields.CreatedAt"/> and
    /// <see cref="ExportGrantSortFields.ExpiresAt"/>, newest first when none is named, with ties broken by the row id.
    /// </summary>
    public static readonly SortMap<ExportGrantRow> Map = new SortMapBuilder<ExportGrantRow>()
        .By(ExportGrantSortFields.AccountName, row => row.AccountName)
        .By(ExportGrantSortFields.CreatedAt, row => row.CreatedAt)
        .By(ExportGrantSortFields.ExpiresAt, row => row.ExpiresAt)
        .DefaultTo(ExportGrantSortFields.CreatedAt, SortDirection.Descending)
        .TieBreaker(row => row.Id);
}
