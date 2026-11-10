using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.AppsService;
using Maran.Modules.Apps.Common;
using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Mappers;
using Maran.Modules.Apps.Models;
using Maran.Modules.Apps.Persistence;
using Maran.Modules.Apps.Services;
using Maran.SharedKernel.Extensions;

namespace Maran.Modules.Apps.Queries.ListApps;

/// <summary>
/// Handles <see cref="ListAppsQuery"/>: merges what the host reports with the panel's ledger, then
/// searches, filters, orders and pages the result in memory (issue #517).
/// </summary>
/// <remarks>
/// <para>
/// <b>The host is read every time, the ledger fills in what only the panel knows.</b> The version and the
/// service status are probed (<c>ListApps</c>), so an app installed or stopped from a shell is shown as it
/// is; the ledger adds the state of an install or removal in flight, the task that carries it and when the
/// app was first seen.
/// </para>
/// <para>
/// <b>Which rows appear.</b> Every app the host has; and a ledger row the host does not have only while
/// something is happening to it or went wrong (installing, removing, failed). An installed row the host
/// no longer has is an app removed from a shell since the last start-up pass, and showing it as installed
/// would be the panel contradicting the server.
/// </para>
/// <para>
/// <b>In memory, and that is right here:</b> the catalogue is closed and short (two web servers, six PHP
/// and four Node.js versions), the agent answers it whole, and the search is over text only this handler
/// holds (the localized name).
/// </para>
/// </remarks>
public sealed class ListAppsQueryHandler
{
    /// <summary>The text members a search looks in: the id and the localized name.</summary>
    private static readonly IReadOnlyList<Func<AppRow, string>> SearchFields =
    [
        app =>
        {
            return app.AppId;
        },
        app =>
        {
            return app.Name;
        },
    ];

    /// <summary>The agent's app store surface.</summary>
    private readonly IAgentAppsClient _agent;

    /// <summary>The Apps module's database context.</summary>
    private readonly AppsDbContext _dbContext;

    /// <summary>Names an app, its kind, state and status in the request's language.</summary>
    private readonly AppDisplayNames _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="agent">The agent's app store surface.</param>
    /// <param name="dbContext">The Apps module's database context.</param>
    /// <param name="names">Names an app, its kind, state and status in the request's language.</param>
    public ListAppsQueryHandler(IAgentAppsClient agent, AppsDbContext dbContext, AppDisplayNames names)
    {
        _agent = agent;
        _dbContext = dbContext;
        _names = names;
    }

    /// <summary>Returns the requested page of apps.</summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The page with the total that matched, or the agent's own typed failure.</returns>
    public async Task<Result<PagedResult<AppDto>>> HandleAsync(ListAppsQuery query, CancellationToken cancellationToken)
    {
        var hostApps = await _agent.ListAppsAsync(cancellationToken);
        if (!hostApps.IsSuccess)
        {
            return Result<PagedResult<AppDto>>.Fail(hostApps.Error!);
        }

        var ledger = await _dbContext.InstalledApps
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        IEnumerable<AppRow> rows = Merge(hostApps.Value, ledger);

        if (query.Kind is { } kind)
        {
            rows = rows.Where(app => { return app.Kind == kind; });
        }

        if (query.State is { } state)
        {
            rows = rows.Where(app => { return app.State == state; });
        }

        var page = rows.ToPaged(query, ListAppsSorts.Map, SearchFields);

        return Result<PagedResult<AppDto>>.Ok(page.Map(app =>
        {
            return new AppDto(
                app.AppId,
                app.Name,
                app.Kind,
                _names.Of(app.Kind),
                app.Version,
                app.State,
                _names.Of(app.State),
                app.ServiceStatus,
                _names.Of(app.ServiceStatus),
                app.InstalledAt,
                app.TaskId);
        }));
    }

    /// <summary>Merges the host's apps with the ledger's rows.</summary>
    /// <param name="hostApps">What the host reports.</param>
    /// <param name="ledger">The ledger's rows.</param>
    /// <returns>One row per app the list shows.</returns>
    private List<AppRow> Merge(IReadOnlyList<AgentApp> hostApps, List<InstalledApp> ledger)
    {
        var rows = new List<AppRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var host in hostApps)
        {
            if (!AppId.TryParse(host.AppId, out var appId))
            {
                // An id outside the catalogue (a newer agent with a larger set) is not an app this panel
                // can name, install or remove.
                continue;
            }

            var row = ledger.FirstOrDefault(app => { return app.AppId == appId.Value; });
            seen.Add(appId.Value);

            rows.Add(new AppRow(
                appId.Value,
                _names.Of(appId),
                appId.Kind,
                host.Version,
                row?.State ?? AppState.Installed,
                AgentAppMapper.ToServiceStatus(host.ServiceStatus),
                row?.InstalledAt,
                row?.TaskId));
        }

        foreach (var row in ledger)
        {
            if (seen.Contains(row.AppId) || row.IsInstalled || !AppId.TryParse(row.AppId, out var appId))
            {
                continue;
            }

            rows.Add(new AppRow(
                appId.Value,
                _names.Of(appId),
                appId.Kind,
                row.Version,
                row.State,
                AppServiceStatus.None,
                row.InstalledAt,
                row.TaskId));
        }

        return rows;
    }
}
