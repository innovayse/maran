using Maran.Modules.Apps.Models;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Apps.Queries.ListApps;

/// <summary>
/// The orders the app list accepts. <see cref="ListAppsQueryValidator"/> refuses any other sort and
/// <see cref="ListAppsQueryHandler"/> orders by exactly these, so the whitelist of sorts is written once.
/// </summary>
public static class ListAppsSorts
{
    /// <summary>
    /// The fields of <see cref="AppSortFields"/>: by kind when none is named, so the web server comes before
    /// the runtimes, then by the catalogue id, with ties broken by the catalogue id.
    /// </summary>
    /// <remarks>
    /// Over the merged row because the whole list is read from the agent and the ledger and paged in memory;
    /// there is no stored row to order. A text member is compared ordinally, so the order does not move with
    /// the server's culture.
    /// </remarks>
    public static readonly SortMap<AppRow> Map = new SortMapBuilder<AppRow>()
        .By(AppSortFields.AppId, app => app.AppId)
        .By(AppSortFields.Name, app => app.Name)
        .By(AppSortFields.Kind, app => app.Kind)
        .By(AppSortFields.State, app => app.State)
        .By(AppSortFields.InstalledAt, app => app.InstalledAt)
        .DefaultTo(AppSortFields.Kind, SortDirection.Ascending)
        .TieBreaker(app => app.AppId);
}
