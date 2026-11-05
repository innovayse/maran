using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Domain.Policies;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Monitoring.Queries.ListPendingUpdates;

/// <summary>
/// The orders the pending updates list accepts. <see cref="ListPendingUpdatesQueryValidator"/> refuses
/// any other sort and <see cref="ListPendingUpdatesQueryHandler"/> orders by exactly these, so the
/// whitelist is written once.
/// </summary>
public static class ListPendingUpdatesSorts
{
    /// <summary>
    /// The sortable fields <see cref="PendingUpdateSortFields.PackageName"/>,
    /// <see cref="PendingUpdateSortFields.Source"/> and <see cref="PendingUpdateSortFields.Security"/>:
    /// by package name ascending when none is named, with ties broken by the package name.
    /// </summary>
    /// <remarks>
    /// Over the agent's row because the whole list is read from the agent and paged in memory; there is
    /// no stored row to order. A text member is compared ordinally ignoring case, so the order does not
    /// move with the server's culture.
    /// </remarks>
    public static readonly SortMap<AgentPendingUpdate> Map = new SortMapBuilder<AgentPendingUpdate>()
        .By(PendingUpdateSortFields.PackageName, update => update.PackageName)
        .By(PendingUpdateSortFields.Source, update => update.Source)
        .By(PendingUpdateSortFields.Security, update => PendingUpdateClassificationPolicy.RankOf(update.Classification))
        .DefaultTo(PendingUpdateSortFields.PackageName, SortDirection.Ascending)
        .TieBreaker(update => update.PackageName);
}
