using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Queries.ListAccountExports;

/// <summary>
/// The orders the account exports list accepts. <see cref="ListAccountExportsQueryValidator"/> refuses any other
/// sort and <see cref="ListAccountExportsQueryHandler"/> orders by exactly these.
/// </summary>
public static class ListAccountExportsSorts
{
    /// <summary>
    /// <see cref="AccountExportSortFields.AccountName"/>, <see cref="AccountExportSortFields.SizeBytes"/>,
    /// <see cref="AccountExportSortFields.CreatedAt"/> and <see cref="AccountExportSortFields.ExpiresAt"/>, newest
    /// first when none is named, with ties broken by the row id.
    /// </summary>
    public static readonly SortMap<AccountExport> Map = new SortMapBuilder<AccountExport>()
        .By(AccountExportSortFields.AccountName, export => export.SourceAccountName)
        .By(AccountExportSortFields.SizeBytes, export => export.SizeBytes)
        .By(AccountExportSortFields.CreatedAt, export => export.CreatedAt)
        .By(AccountExportSortFields.ExpiresAt, export => export.ExpiresAt)
        .DefaultTo(AccountExportSortFields.CreatedAt, SortDirection.Descending)
        .TieBreaker(export => export.Id);
}
