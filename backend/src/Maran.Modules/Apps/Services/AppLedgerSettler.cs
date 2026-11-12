using Maran.Agent.Client.Services.AppsService;
using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.Policies;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Persistence;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Apps.Services;

/// <summary>
/// Judges ledger rows against what the host reports and applies the verdicts (issues #517, #533). The one
/// place the rule "the host wins" is applied; the start-up pass (<see cref="StartupAppReconciler"/>) judges
/// every row, the runtime pass (<see cref="StaleOperationSettler"/>) only the rows whose operation is over.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two saves, releasing before taking.</b> The ledger allows one active site web server (issue #526),
/// and the order in which one save applies its inserts, updates and deletes is the database's to choose,
/// so a pass that removes the old web server's row and records the new one in a single save can be
/// refused. The first save therefore holds only what releases a place (rows deleted, installs marked
/// failed); the second holds what takes one (rows marked installed, apps found on the host).
/// </para>
/// <para>
/// <b>One web server is recorded.</b> A host that reports two web servers holds two sets of ports, which
/// the ledger cannot say: the one the ledger already holds as active stays, and the other is left out
/// and logged, rather than the whole pass failing on it for ever. The holder is read from EVERY row, also
/// those a runtime pass does not judge, so a subset cannot take a place another row holds.
/// </para>
/// </remarks>
public sealed class AppLedgerSettler
{
    /// <summary>Pre-compiled log delegate for a second web server the ledger cannot record.</summary>
    private static readonly Action<ILogger, string, string, Exception?> LogWebServerSkipped =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(AppLedgerSettler)),
            "The host has the web server {AppId} beside {HolderId}; the ledger records one, so it is left out");

    /// <summary>The ledger.</summary>
    private readonly AppsDbContext _dbContext;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where a skipped web server is reported.</summary>
    private readonly ILogger _logger;

    /// <summary>Creates the settler.</summary>
    /// <param name="dbContext">The ledger.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="logger">Where a skipped web server is reported.</param>
    public AppLedgerSettler(AppsDbContext dbContext, IClock clock, ILogger logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Indexes the host's apps by catalogue id.</summary>
    /// <param name="apps">What the host reports.</param>
    /// <returns>
    /// The apps by id. An id outside the catalogue (a newer agent with a larger set) is not an app this
    /// panel can name, so it is left out rather than recorded.
    /// </returns>
    public static Dictionary<string, (AppId Id, string Version)> HostAppsById(IEnumerable<AgentApp> apps)
    {
        var onHost = new Dictionary<string, (AppId Id, string Version)>(StringComparer.Ordinal);
        foreach (var app in apps)
        {
            if (AppId.TryParse(app.AppId, out var appId))
            {
                onHost[appId.Value] = (appId, app.Version);
            }
        }

        return onHost;
    }

    /// <summary>Judges every row and every host app: the start-up pass.</summary>
    /// <param name="onHost">The host's apps by catalogue id.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>What the pass changed.</returns>
    public Task<AppSettlement> SettleAllAsync(
        Dictionary<string, (AppId Id, string Version)> onHost,
        CancellationToken cancellationToken)
    {
        return SettleAsync(onHost, _ => { return true; }, discover: true, cancellationToken);
    }

    /// <summary>Judges only the named operations, and records nothing the host has that the ledger lacks: the runtime pass.</summary>
    /// <param name="onHost">The host's apps by catalogue id.</param>
    /// <param name="observedTasks">
    /// The task each row to judge held when its task was asked about, by row identity. A row is judged only
    /// while it is STILL installing or removing on that same task: the rows are read again here, after the
    /// task was asked about, and one that has moved on since (settled by its own runner, or begun anew on a
    /// new task) is a different operation from the one found over, and is left alone.
    /// </param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>What the pass changed.</returns>
    public Task<AppSettlement> SettleRowsAsync(
        Dictionary<string, (AppId Id, string Version)> onHost,
        IReadOnlyDictionary<Guid, Guid> observedTasks,
        CancellationToken cancellationToken)
    {
        return SettleAsync(
            onHost,
            row =>
            {
                return row.State is AppState.Installing or AppState.Removing
                    && observedTasks.TryGetValue(row.Id, out var observed)
                    && row.TaskId == observed;
            },
            discover: false,
            cancellationToken);
    }

    /// <summary>Judges the chosen rows (and the host's apps when asked) once, and saves in an order the web server index accepts.</summary>
    /// <param name="onHost">The host's apps by catalogue id.</param>
    /// <param name="judged">Whether a row is judged by this pass.</param>
    /// <param name="discover">Whether an app the host has that the ledger lacks is recorded.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>What the pass changed.</returns>
    private async Task<AppSettlement> SettleAsync(
        Dictionary<string, (AppId Id, string Version)> onHost,
        Func<InstalledApp, bool> judged,
        bool discover,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var rows = await _dbContext.InstalledApps.ToListAsync(cancellationToken);

        var failed = 0;
        var removed = 0;
        var toInstall = new List<(InstalledApp Row, string Version)>();
        foreach (var row in rows.Where(judged))
        {
            onHost.TryGetValue(row.AppId, out var hostApp);
            var hostVersion = hostApp.Version;
            switch (AppLedgerPolicy.Decide(row, hostVersion))
            {
                case AppLedgerVerdict.MarkInstalled:
                    toInstall.Add((row, hostVersion!));
                    break;
                case AppLedgerVerdict.MarkFailed:
                    row.MarkFailed(now);
                    failed++;
                    break;
                case AppLedgerVerdict.Delete:
                    _dbContext.InstalledApps.Remove(row);
                    removed++;
                    break;
                case AppLedgerVerdict.Keep:
                    break;
            }
        }

        // First save: what releases a place.
        await _dbContext.SaveChangesAsync(cancellationToken);

        var holder = rows.FirstOrDefault(row =>
        {
            return _dbContext.Entry(row).State != EntityState.Detached && HoldsWebServerPorts(row);
        })?.AppId;

        var installed = 0;
        foreach (var (row, version) in toInstall.OrderByDescending(entry => { return HoldsWebServerPorts(entry.Row); }))
        {
            if (!TakeWebServerPlace(row.Kind, row.AppId, ref holder))
            {
                LogWebServerSkipped(_logger, row.AppId, holder!, null);
                continue;
            }

            row.MarkInstalled(version, now);
            installed++;
        }

        var found = discover ? Discover(rows, onHost, ref holder, now) : 0;

        // Second save: what takes a place.
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new AppSettlement(found, installed, failed, removed);
    }

    /// <summary>Records every app the host has that the ledger has no row for.</summary>
    /// <param name="rows">The ledger's rows.</param>
    /// <param name="onHost">The host's apps by catalogue id.</param>
    /// <param name="holder">The id of the web server that holds the place, or null.</param>
    /// <param name="now">The current instant.</param>
    /// <returns>How many apps were recorded.</returns>
    private int Discover(
        List<InstalledApp> rows,
        Dictionary<string, (AppId Id, string Version)> onHost,
        ref string? holder,
        DateTimeOffset now)
    {
        var known = rows.Select(row =>
        {
            return row.AppId;
        }).ToHashSet(StringComparer.Ordinal);
        var found = 0;
        foreach (var (appIdText, (appId, version)) in onHost)
        {
            if (known.Contains(appIdText))
            {
                continue;
            }

            if (!TakeWebServerPlace(appId.Kind, appIdText, ref holder))
            {
                LogWebServerSkipped(_logger, appIdText, holder!, null);
                continue;
            }

            // Found on the host with no row: put there by hand, or by a panel that predates the app store.
            _dbContext.InstalledApps.Add(InstalledApp.Discover(Guid.NewGuid(), appId, version, now));
            found++;
        }

        return found;
    }

    /// <summary>Whether a row is a web server that holds the ports (installing, installed or being removed).</summary>
    /// <param name="row">The ledger row.</param>
    /// <returns>True when the active web server index counts the row.</returns>
    private static bool HoldsWebServerPorts(InstalledApp row)
    {
        return row.Kind == AppKind.WebServer && row.State is AppState.Installing or AppState.Installed or AppState.Removing;
    }

    /// <summary>Claims the one web server place for an app, when it is a web server.</summary>
    /// <param name="kind">What the app is for.</param>
    /// <param name="appId">The catalogue id.</param>
    /// <param name="holder">The id of the web server that holds the place, or null; set when this app takes it.</param>
    /// <returns>False when the app is a web server and another one holds the place.</returns>
    private static bool TakeWebServerPlace(AppKind kind, string appId, ref string? holder)
    {
        if (kind != AppKind.WebServer)
        {
            return true;
        }

        if (holder is not null && !string.Equals(holder, appId, StringComparison.Ordinal))
        {
            return false;
        }

        holder = appId;
        return true;
    }
}
