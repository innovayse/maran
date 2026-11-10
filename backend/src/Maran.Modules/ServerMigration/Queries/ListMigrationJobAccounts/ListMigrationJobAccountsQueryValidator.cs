using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.ServerMigration.Queries.ListMigrationJobAccounts;

/// <summary>Holds the account list to the paging bounds.</summary>
public sealed class ListMigrationJobAccountsQueryValidator : PagedQueryValidator<ListMigrationJobAccountsQuery, MigrationJobAccount>
{
    /// <summary>Binds the validator to the sort whitelist.</summary>
    public ListMigrationJobAccountsQueryValidator()
        : base(ListMigrationJobAccountsSorts.Map)
    {
    }
}
