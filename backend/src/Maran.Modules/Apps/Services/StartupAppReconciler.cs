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

        var now = _clock.UtcNow;
        var rows = await dbContext.InstalledApps.ToListAsync(cancellationToken);

        var installed = 0;
        var failed = 0;
        var removed = 0;
        foreach (var row in rows)
        {
            onHost.TryGetValue(row.AppId, out var hostApp);
            var hostVersion = hostApp.Version;
            switch (AppLedgerPolicy.Decide(row, hostVersion))
            {
                case AppLedgerVerdict.MarkInstalled:
                    row.MarkInstalled(hostVersion!, now);
                    installed++;
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

            // Found on the host with no row: put there by hand, or by a panel that predates the app store.
            dbContext.InstalledApps.Add(InstalledApp.Discover(Guid.NewGuid(), appId, version, now));
            found++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        LogReconciled(_logger, found, installed, failed, removed, null);
        return true;
    }

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
