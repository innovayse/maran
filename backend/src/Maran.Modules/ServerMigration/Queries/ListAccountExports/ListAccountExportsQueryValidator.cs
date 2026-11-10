using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.ServerMigration.Queries.ListAccountExports;

/// <summary>Holds <see cref="ListAccountExportsQuery"/> to the panel's paging bounds (rules/security.md "Input").</summary>
public sealed class ListAccountExportsQueryValidator : PagedQueryValidator<ListAccountExportsQuery, AccountExport>
{
    /// <summary>Declares the paging rules against <see cref="ListAccountExportsSorts.Map"/>.</summary>
    public ListAccountExportsQueryValidator()
        : base(ListAccountExportsSorts.Map)
    {
    }
}
