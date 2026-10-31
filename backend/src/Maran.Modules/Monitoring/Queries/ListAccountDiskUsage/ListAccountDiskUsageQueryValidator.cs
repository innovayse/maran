using Maran.Sdk.Contracts;
using Maran.Sdk.Validators;

namespace Maran.Modules.Monitoring.Queries.ListAccountDiskUsage;

/// <summary>
/// Holds <see cref="ListAccountDiskUsageQuery"/> to the panel's paging bounds (rules/security.md "Input").
/// </summary>
/// <remarks>
/// The list has no filter beyond the search, which its binder already holds to its own bounds, so there
/// is nothing beyond the paging contract for this validator to add.
/// </remarks>
public sealed class ListAccountDiskUsageQueryValidator
    : PagedQueryValidator<ListAccountDiskUsageQuery, AccountSnapshot>
{
    /// <summary>Declares the paging rules against <see cref="ListAccountDiskUsageSorts.Map"/>.</summary>
    public ListAccountDiskUsageQueryValidator()
        : base(ListAccountDiskUsageSorts.Map)
    {
    }
}
