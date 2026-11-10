using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Queries.ListAccountImports;

/// <summary>
/// The orders the account imports list accepts. <see cref="ListAccountImportsQueryValidator"/> refuses any other
/// sort and <see cref="ListAccountImportsQueryHandler"/> orders by exactly these.
/// </summary>
public static class ListAccountImportsSorts
{
    /// <summary>
    /// <see cref="AccountImportSortFields.AccountName"/>, <see cref="AccountImportSortFields.SourceHostName"/>,
    /// <see cref="AccountImportSortFields.State"/>, <see cref="AccountImportSortFields.SizeBytes"/> and
    /// <see cref="AccountImportSortFields.CreatedAt"/>, newest first when none is named, with ties broken by the row id.
    /// </summary>
    public static readonly SortMap<AccountImport> Map = new SortMapBuilder<AccountImport>()
        .By(AccountImportSortFields.AccountName, import => import.SourceAccountName)
        .By(AccountImportSortFields.SourceHostName, import => import.SourceHostName)
        .By(AccountImportSortFields.State, import => import.State)
        .By(AccountImportSortFields.SizeBytes, import => import.ArtifactSizeBytes)
        .By(AccountImportSortFields.CreatedAt, import => import.CreatedAt)
        .DefaultTo(AccountImportSortFields.CreatedAt, SortDirection.Descending)
        .TieBreaker(import => import.Id);
}
