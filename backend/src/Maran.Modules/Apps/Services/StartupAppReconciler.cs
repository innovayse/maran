using Maran.Agent.Client.Interfaces;
using Maran.Modules.Apps.Domain.Entities;
using Maran.Modules.Apps.Domain.Enums;
using Maran.Modules.Apps.Domain.Policies;
using Maran.Modules.Apps.Domain.ValueObjects;
using Maran.Modules.Apps.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Apps.Services;

/// <summary>
/// Settles the ledger of installed apps against what the host reports, once, after the panel has started
/// (issue #517).
/// </summary>
/// <remarks>
/// <para>
/// <b>The host wins.</b> The ledger is the panel's memory of what it asked for; the host is what is true.
/// A panel that stopped between "installing" and the end of an install, an app removed from a shell and one
/// put there by a shell all leave the ledger saying something the host does not. Each row is judged by
/// <see cref="AppLedgerPolicy"/>, and every app the host has that the ledger lacks is recorded as found.
/// Without this pass the stack gate, which reads the ledger, would refuse sites on a server whose web
/// server is installed (a host upgraded from a panel that installed one by itself) until an administrator
/// installed it again.
/// </para>
/// <para>
/// <b>An agent that is not up yet is expected.</b> The panel and the agent are separate units and there is
/// no ordering guarantee that survives a reboot, so the first pass may find nothing listening. It retries a
/// bounded number of times and then stops: a pass that has failed <see cref="MaximumAttempts"/> times is a
/// broken host rather than a slow start, and a service that retried for ever would hide that behind an
/// error line every thirty seconds.
/// </para>
/// <para>
/// <b>Not journalled.</b> Settling the ledger is not a decision of anyone's: it restates what the host
/// already did, and an entry per restart per app would bury the entries that record decisions.
/// </para>
/// </remarks>
public sealed class StartupAppReconciler : BackgroundService
{
    /// <summary>How many times a failed pass is retried before the reconciler gives up.</summary>
    public const int MaximumAttempts = 5;

    /// <summary>How many times one pass reads the ledger again after a save lost a race, before it counts as a failed attempt.</summary>
    public const int MaximumConflictPasses = 3;

    /// <summary>How long between attempts.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    private static readonly Action<ILogger, int, int, int, int, Exception?> LogReconciled =
        LoggerMessage.Define<int, int, int, int>(
            LogLevel.Information,
            new EventId(1, nameof(StartupAppReconciler)),
            "Settled the app ledger against the host: {Found} found, {Installed} marked installed, "
            + "{Failed} marked failed, {Removed} removed");

    /// <summary>Pre-compiled log delegate for a pass the agent would not complete.</summary>
    private static readonly Action<ILogger, int, Exception?> LogAttemptFailed =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(2, nameof(StartupAppReconciler)),
            "Could not settle the app ledger against the host on attempt {Attempt}; sites stay refused until it is");

    /// <summary>Pre-compiled log delegate for a save that lost a race and was judged again.</summary>
    private static readonly Action<ILogger, int, Exception?> LogConflictRetried =
        LoggerMessage.Define<int>(
            LogLevel.Information,
            new EventId(4, nameof(StartupAppReconciler)),
            "The app ledger changed while it was being settled (pass {Pass}); reading it again");

    /// <summary>Pre-compiled log delegate for a second web server the ledger cannot record.</summary>
    private static readonly Action<ILogger, string, string, Exception?> LogWebServerSkipped =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(5, nameof(StartupAppReconciler)),
            "The host has the web server {AppId} beside {HolderId}; the ledger records one, so it is left out");

    /// <summary>Pre-compiled log delegate for giving up.</summary>
    private static readonly Action<ILogger, int, Exception?> LogGaveUp =
        LoggerMessage.Define<int>(
            LogLevel.Error,
            new EventId(3, nameof(StartupAppReconciler)),
            "Gave up settling the app ledger after {Attempts} attempts; restart the panel once the agent answers");

    /// <summary>Opens one scope per pass to resolve the module's scoped services from.</summary>
    /// <remarks>
    /// A scope FACTORY, not an <c>AppsDbContext</c>. A <see cref="BackgroundService"/> is a singleton, the
    /// context is scoped, and a singleton capturing a scoped dependency is refused by the container at BUILD
    /// time, which stops the whole API rather than degrading one feature.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<StartupAppReconciler> _logger;

    /// <summary>Creates the reconciler.</summary>
    /// <param name="scopeFactory">Opens the scope each pass resolves its dependencies from.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="logger">Where the outcome of each pass is reported.</param>
    public StartupAppReconciler(
        IServiceScopeFactory scopeFactory,
        IClock clock,
        ILogger<StartupAppReconciler> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Settles the ledger against the host, once.</summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>True when the host was asked and the ledger settled; false when the agent would not answer.</returns>
    /// <remarks>
    /// Public because it is the pass, and the pass is what has behaviour worth asserting: a test drives it
    /// directly rather than starting a hosted service and waiting for a timer, which is the sleep
    /// rules/testing.md forbids.
    /// </remarks>
    public async Task<bool> ReconcileAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppsDbContext>();
        var agent = scope.ServiceProvider.GetRequiredService<IAgentAppsClient>();

        var apps = await agent.ListAppsAsync(cancellationToken);
        if (!apps.IsSuccess)
        {
            return false;
        }

        // The host's apps by catalogue id. An id outside the catalogue (a newer agent with a larger set) is
        // not an app this panel can name, so it is left out rather than recorded.
        var onHost = new Dictionary<string, (AppId Id, string Version)>(StringComparer.Ordinal);
        foreach (var app in apps.Value)
        {
            if (AppId.TryParse(app.AppId, out var appId))
            {
                onHost[appId.Value] = (appId, app.Version);
            }
        }

        // A save the database refused because another writer changed the ledger (a lost race on a row or on
        // the web server index) is not a broken host: the pass reads the ledger again and judges it afresh.
        // Only the last attempt lets the conflict escape, to be counted as a failed attempt by the caller.
        Settlement settlement;
        for (var pass = 1; ; pass++)
        {
            try
            {
                settlement = await SettleAsync(dbContext, onHost, cancellationToken);
                break;
            }
            catch (DbUpdateException exception) when (
                pass < MaximumConflictPasses && AppLedgerConflicts.Classify(exception) is not null)
            {
                dbContext.ChangeTracker.Clear();
                LogConflictRetried(_logger, pass, null);
            }
        }

        LogReconciled(_logger, settlement.Found, settlement.Installed, settlement.Failed, settlement.Removed, null);
        return true;
    }

    /// <summary>Judges every row and every host app once, and saves in an order the web server index accepts.</summary>
    /// <param name="dbContext">The ledger.</param>
    /// <param name="onHost">The host's apps by catalogue id.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>What the pass changed.</returns>
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
    /// and logged, rather than the whole pass failing on it for ever.
    /// </para>
    /// </remarks>
    private async Task<Settlement> SettleAsync(
        AppsDbContext dbContext,
        Dictionary<string, (AppId Id, string Version)> onHost,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var rows = await dbContext.InstalledApps.ToListAsync(cancellationToken);

        var failed = 0;
        var removed = 0;
        var toInstall = new List<(InstalledApp Row, string Version)>();
        foreach (var row in rows)
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
                    dbContext.InstalledApps.Remove(row);
                    removed++;
                    break;
                case AppLedgerVerdict.Keep:
                    break;
            }
        }

        // First save: what releases a place.
        await dbContext.SaveChangesAsync(cancellationToken);

        var holder = rows.FirstOrDefault(row =>
        {
            return dbContext.Entry(row).State != EntityState.Detached && HoldsWebServerPorts(row);
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
            dbContext.InstalledApps.Add(InstalledApp.Discover(Guid.NewGuid(), appId, version, now));
            found++;
        }

        // Second save: what takes a place.
        await dbContext.SaveChangesAsync(cancellationToken);

        return new Settlement(found, installed, failed, removed);
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

    /// <summary>What one pass changed, for the log line.</summary>
    /// <param name="Found">Apps recorded because the host had them.</param>
    /// <param name="Installed">Rows marked installed.</param>
    /// <param name="Failed">Rows marked failed.</param>
    /// <param name="Removed">Rows removed.</param>
    private readonly record struct Settlement(int Found, int Installed, int Failed, int Removed);

    /// <summary>Runs passes until one succeeds or the attempts run out.</summary>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                if (await AttemptAsync(attempt, stoppingToken))
                {
                    return;
                }

                if (attempt < MaximumAttempts)
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }

            LogGaveUp(_logger, MaximumAttempts, null);
        }
        catch (OperationCanceledException)
        {
            // Shutdown. Not a failure, and deliberately not logged as one.
        }
    }

    /// <summary>Runs one pass, turning anything it throws into a failed attempt.</summary>
    /// <param name="attempt">Which attempt this is, so the log line names it.</param>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    /// <returns>True when the pass settled the ledger.</returns>
    /// <remarks>
    /// A database that is not reachable yet, or a socket that is not there, arrives here as an exception
    /// rather than as a failed <c>Result</c>. Letting it escape <see cref="ExecuteAsync"/> would stop the
    /// service for the lifetime of the process.
    /// </remarks>
    private async Task<bool> AttemptAsync(int attempt, CancellationToken stoppingToken)
    {
        try
        {
            if (await ReconcileAsync(stoppingToken))
            {
                return true;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogAttemptFailed(_logger, attempt, exception);
            return false;
        }

        LogAttemptFailed(_logger, attempt, null);
        return false;
    }
}
