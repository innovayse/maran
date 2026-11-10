using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.ServerMigration.Queries.ListMigrationJobAccounts;

/// <summary>The sort whitelist of the account list.</summary>
public static class ListMigrationJobAccountsSorts
{
    /// <summary>The whitelist, by name by default.</summary>
    public static readonly SortMap<MigrationJobAccount> Map = new SortMapBuilder<MigrationJobAccount>()
        .By(MigrationJobAccountSortFields.Name, account => account.SourceAccountName)
        .By(MigrationJobAccountSortFields.State, account => account.State)
        .By(MigrationJobAccountSortFields.Size, account => account.SourceDiskBytes)
        .DefaultTo(MigrationJobAccountSortFields.Name, SortDirection.Ascending)
        .TieBreaker(account => account.Id);
}
