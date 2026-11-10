using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.ServerMigration.Queries.ListAccountImports;

/// <summary>Holds <see cref="ListAccountImportsQuery"/> to the panel's paging bounds (rules/security.md "Input").</summary>
/// <remarks>
/// The state filter needs no rule of its own: it is an exact match against the names of a closed set, so a spelling
/// that is none of them matches nothing, which is the honest answer to a state no import can be in.
/// </remarks>
public sealed class ListAccountImportsQueryValidator : PagedQueryValidator<ListAccountImportsQuery, AccountImport>
{
    /// <summary>Declares the paging rules against <see cref="ListAccountImportsSorts.Map"/>.</summary>
    public ListAccountImportsQueryValidator()
        : base(ListAccountImportsSorts.Map)
    {
    }
}
