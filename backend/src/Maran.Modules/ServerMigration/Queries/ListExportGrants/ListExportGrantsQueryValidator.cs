using Maran.Modules.ServerMigration.Models;
using Maran.Sdk.Validators;

namespace Maran.Modules.ServerMigration.Queries.ListExportGrants;

/// <summary>Holds <see cref="ListExportGrantsQuery"/> to the panel's paging bounds (rules/security.md "Input").</summary>
/// <remarks>The state filter is an exact match against the names of a closed set, so a spelling that is none of them matches nothing.</remarks>
public sealed class ListExportGrantsQueryValidator : PagedQueryValidator<ListExportGrantsQuery, ExportGrantRow>
{
    /// <summary>Declares the paging rules against <see cref="ListExportGrantsSorts.Map"/>.</summary>
    public ListExportGrantsQueryValidator()
        : base(ListExportGrantsSorts.Map)
    {
    }
}
