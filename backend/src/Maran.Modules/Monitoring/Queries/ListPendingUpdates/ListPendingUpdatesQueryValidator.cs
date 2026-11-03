using Maran.Agent.Client.Services.MonitorService;
using Maran.Sdk.Validators;

namespace Maran.Modules.Monitoring.Queries.ListPendingUpdates;

/// <summary>
/// Holds <see cref="ListPendingUpdatesQuery"/> to the panel's paging bounds (rules/security.md "Input").
/// </summary>
/// <remarks>
/// The list has no filter beyond the search, which its binder already holds to its own bounds, so there
/// is nothing beyond the paging contract for this validator to add.
/// </remarks>
public sealed class ListPendingUpdatesQueryValidator
    : PagedQueryValidator<ListPendingUpdatesQuery, AgentPendingUpdate>
{
    /// <summary>Declares the paging rules against <see cref="ListPendingUpdatesSorts.Map"/>.</summary>
    public ListPendingUpdatesQueryValidator()
        : base(ListPendingUpdatesSorts.Map)
    {
    }
}
