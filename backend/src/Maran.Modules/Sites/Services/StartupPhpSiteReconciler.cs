using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Converges every stored PHP site's pool and vhost to what the current agent would render, once,
/// when the panel starts.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what moves a site created before the PHP sockets changed place.</b> A pool and a vhost
/// written by an older agent name the old socket directory. Nothing re-renders them on its own, and
/// the symptom is a site answering 502 after an upgrade. The agent's same-version
/// <c>UpdateSitePhpVersion</c> converges the pool and the vhost to the current templates, writing only
/// what differs; this pass asks it to do that for every PHP site, using the version the panel already
/// stores.
/// </para>
/// <para>
/// <b>It carries the customer's PHP settings.</b> The agent's converge does NOT keep the settings
/// already on disk: it renders the pool from exactly the overrides it is given and rewrites the file
/// whenever that differs from disk, so a setting present on disk but absent from the call is dropped.
/// Every call therefore sends the account's stored settings cut to its plan's bounds
/// (<see cref="PhpAccountPoolSettings"/>), the same answer a site creation, a version switch and a
/// plan change send, so a start-up pass can never undo what they wrote (issue #138).
/// </para>
/// <para>
/// <b>It runs on EVERY start, not once.</b> The panel keeps no record of which site has been
/// converged, deliberately: such a record is a second source of truth that goes stale the moment
/// someone edits a file by hand, and it would be the very thing that made a site look done when it
/// was not. The agent answers a site that is already current without writing or reloading, so a
/// repeat pass costs one round trip per site and nothing else. For the same reason nothing is
/// journalled: no decision is made here and nothing a user did is being recorded.
/// </para>
/// <para>
/// <b>It reads every tenant's sites, on purpose.</b> There is no signed-in user at startup, and a
/// tenant-filtered read would see nothing at all, which looks exactly like a host with no sites. The
/// read ignores the tenant filter and is bounded by the one kind of row it wants (PHP sites); each
/// site is then addressed through its owning account's system user name, joined in memory from
/// <see cref="IAccountDirectory.ListAsync"/>. That call is unscoped by design and is safe here only
/// because this class is not reachable from any request.
/// </para>
/// <para>
/// <b>It never renders from a row it read earlier than it holds the site.</b> The pass lists every PHP site
/// once, while the panel is already serving requests, and then visits them one by one. Each site is rendered
/// under that site's operation lock (<see cref="ISiteOperationLock"/>, issue #561) from a row read again once
/// the lock is held, the way every other writer of a vhost does, so a disable, an options save or a delete that
/// committed during the pass is honoured and not overwritten by a vhost rendered from the listing. A site that
/// stays busy for the whole wait counts as failed, which the pass's own retry then converges.
/// </para>
/// <para>
/// <b>An agent that is not up yet is expected</b>, as for the firewall's startup pass: the panel and
/// the agent are separate units with no ordering guarantee across a reboot. A pass that leaves any
/// site unconverged is retried up to <see cref="MaximumAttempts"/> times and then abandoned with an
/// error line, because a host that fails that many times is broken rather than slow.
/// </para>
/// </remarks>
public sealed class StartupPhpSiteReconciler : BackgroundService
{
    /// <summary>What became of one site in a pass.</summary>
    private enum ConvergeOutcome
    {
        /// <summary>The agent converged the site.</summary>
        Converged,

        /// <summary>The site was deleted, or is no longer PHP, so there is nothing to converge.</summary>
        Gone,

        /// <summary>The agent refused the site or could not be reached.</summary>
        Refused,

        /// <summary>Another operation held the site for the whole wait.</summary>
        Busy,
    }

    /// <summary>How many times a pass that left a site unconverged is retried before giving up.</summary>
    public const int MaximumAttempts = 5;

    /// <summary>How long between attempts.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    private static readonly Action<ILogger, int, int, int, int, Exception?> LogReconciled =
        LoggerMessage.Define<int, int, int, int>(
            LogLevel.Information,
            new EventId(1, nameof(StartupPhpSiteReconciler)),
            "Converged {Converged} of {Total} PHP sites to the current pool and vhost layout; "
            + "{Failed} failed and {Skipped} were skipped because their account is gone or the site itself is gone or no longer PHP");

    /// <summary>Pre-compiled log delegate for a site the agent would not converge.</summary>
    private static readonly Action<ILogger, string, string, Exception?> LogSiteFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2, nameof(StartupPhpSiteReconciler)),
            "Could not converge PHP site {Domain}: {Code}");

    /// <summary>Pre-compiled log delegate for a site whose owning account is not known.</summary>
    private static readonly Action<ILogger, string, Exception?> LogAccountMissing =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3, nameof(StartupPhpSiteReconciler)),
            "Skipped PHP site {Domain}: its owning account does not exist");

    /// <summary>Pre-compiled log delegate for a site that was deleted, or left PHP, since the pass listed it.</summary>
    private static readonly Action<ILogger, string, Exception?> LogSiteGone =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(8, nameof(StartupPhpSiteReconciler)),
            "Skipped PHP site {Domain}: it was deleted, or stopped being a PHP site, since the pass listed it");

    /// <summary>Pre-compiled log delegate for a site that stayed busy for the whole wait.</summary>
    private static readonly Action<ILogger, string, Exception?> LogSiteBusy =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(7, nameof(StartupPhpSiteReconciler)),
            "Could not converge PHP site {Domain}: another operation held it for the whole wait");

    /// <summary>Pre-compiled log delegate for a pass that found PHP sites and no installed site web server.</summary>
    private static readonly Action<ILogger, Exception?> LogStackNotReady =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(6, nameof(StartupPhpSiteReconciler)),
            "PHP sites exist but no site web server is installed yet; their vhosts cannot be converged until it is");

    /// <summary>Pre-compiled log delegate for a pass the agent would not complete.</summary>
    private static readonly Action<ILogger, int, Exception?> LogAttemptFailed =
        LoggerMessage.Define<int>(
            LogLevel.Warning,
            new EventId(4, nameof(StartupPhpSiteReconciler)),
            "Could not converge every PHP site on attempt {Attempt}; older sites may keep answering 502");

    /// <summary>Pre-compiled log delegate for giving up.</summary>
    private static readonly Action<ILogger, int, Exception?> LogGaveUp =
        LoggerMessage.Define<int>(
            LogLevel.Error,
            new EventId(5, nameof(StartupPhpSiteReconciler)),
            "Gave up converging PHP sites after {Attempts} attempts; sites created by an older agent "
            + "may keep answering 502 until the panel is restarted or each site's PHP version is re-applied");

    /// <summary>Opens one scope per pass to resolve the module's scoped services from.</summary>
    /// <remarks>
    /// A scope FACTORY, not a <see cref="SitesDbContext"/>: a <see cref="BackgroundService"/> is a
    /// singleton and the context is scoped, and a singleton capturing a scoped dependency is refused
    /// by the container at build time, which stops the whole API.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<StartupPhpSiteReconciler> _logger;

    /// <summary>Creates the reconciler.</summary>
    /// <param name="scopeFactory">Opens the scope each pass resolves its dependencies from.</param>
    /// <param name="logger">Where the outcome of each pass is reported.</param>
    public StartupPhpSiteReconciler(
        IServiceScopeFactory scopeFactory,
        ILogger<StartupPhpSiteReconciler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>Asks the agent to converge every PHP site, once.</summary>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>
    /// True when no site failed — including when there were none, and when sites were skipped for a
    /// missing account, which no retry can repair. False when the agent refused at least one site.
    /// </returns>
    /// <remarks>
    /// Sequential, because the agent serialises work per account and takes its own locks; parallel
    /// calls would only turn into busy answers. One failure does not abandon the pass: a site the
    /// agent cannot converge must not leave every later site on the old socket.
    ///
    /// Public because it is the pass and the pass is what has behaviour worth asserting; a test drives
    /// it directly instead of starting a hosted service and waiting on a timer.
    /// </remarks>
    public async Task<bool> ReconcileAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SitesDbContext>();
        var agent = scope.ServiceProvider.GetRequiredService<IAgentSitesClient>();
        var accountDirectory = scope.ServiceProvider.GetRequiredService<IAccountDirectory>();
        var poolSettings = scope.ServiceProvider.GetRequiredService<PhpAccountPoolSettings>();
        var stack = scope.ServiceProvider.GetRequiredService<SiteStackGate>();
        var locks = scope.ServiceProvider.GetRequiredService<ISiteOperationLock>();

#pragma warning disable RS0030 // startup has no principal; every tenant's PHP sites are converged
        var sites = await dbContext.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(site => site.BackendType == SiteBackendType.Php)
            .OrderBy(site => site.Domain)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        // A vhost is rendered for the installed web server, so with sites to converge and none installed
        // the pass cannot be right: it fails (and is retried) instead of rendering for a guess.
        var webServer = (await stack.ReadAsync(cancellationToken)).WebServer;
        if (sites.Count > 0 && webServer is null)
        {
            LogStackNotReady(_logger, null);
            return false;
        }

        var accounts = (await accountDirectory.ListAsync(cancellationToken))
            .ToDictionary(account =>
            {
                return account.Id;
            });

        // One read per account and not per site: an account's sites share their pools' settings.
        var settingsOfAccount = new Dictionary<Guid, IReadOnlyList<PhpSettingDto>>();
        var converged = 0;
        var failed = 0;
        var skipped = 0;
        foreach (var site in sites)
        {
            if (!accounts.TryGetValue(site.AccountId, out var account))
            {
                // Not a failure: the account is gone or never existed, and asking again changes
                // nothing. Counting it as one would retry the whole pass five times for no reason.
                LogAccountMissing(_logger, site.Domain, null);
                skipped++;
                continue;
            }

            if (!settingsOfAccount.TryGetValue(account.Id, out var settings))
            {
                settings = await poolSettings.EffectiveAsync(account.Id, account.PhpLimits, cancellationToken);
                settingsOfAccount[account.Id] = settings;
            }

            var outcome = await ConvergeGuardedAsync(
                dbContext, locks, agent, site.Id, site.Domain, account, settings, webServer!.Value, cancellationToken);
            switch (outcome)
            {
                case ConvergeOutcome.Converged:
                    converged++;
                    break;
                case ConvergeOutcome.Gone:
                    LogSiteGone(_logger, site.Domain, null);
                    skipped++;
                    break;
                case ConvergeOutcome.Busy:
                    LogSiteBusy(_logger, site.Domain, null);
                    failed++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        LogReconciled(_logger, converged, sites.Count, failed, skipped, null);
        return failed == 0;
    }

    /// <summary>Runs passes until one leaves no site failed or the attempts run out.</summary>
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
            // Shutdown. Not a failure, and not logged as one.
        }
    }

    /// <summary>Converges one site while holding it, from the row as stored once it is held.</summary>
    /// <param name="dbContext">The Sites module's context.</param>
    /// <param name="locks">The shared per-site lock.</param>
    /// <param name="agent">The agent's site operations.</param>
    /// <param name="siteId">The listed PHP site; only its id is trusted.</param>
    /// <param name="domain">The listed site's domain, for the log.</param>
    /// <param name="account">The site's owning account.</param>
    /// <param name="settings">The settings the account's pools carry, cut to its plan's bounds.</param>
    /// <param name="webServer">The installed site web server the vhost is rendered for.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What became of the site.</returns>
    private async Task<ConvergeOutcome> ConvergeGuardedAsync(
        SitesDbContext dbContext,
        ISiteOperationLock locks,
        IAgentSitesClient agent,
        Guid siteId,
        string domain,
        AccountSnapshot account,
        IReadOnlyList<PhpSettingDto> settings,
        SiteWebServer webServer,
        CancellationToken cancellationToken)
    {
        var guarded = await SiteGuard.RunAsync(
            locks,
            siteId,
            SiteLockWait.Job,
            async () =>
            {
                // Read again now that nothing else can change the site: it may have been deleted, or
                // moved to another version or backend, since the pass listed it.
                var site = await SiteRowRefresh.ReadAsync(dbContext, siteId, account.Id, cancellationToken);
                if (site is null || site.BackendType != SiteBackendType.Php)
                {
                    return Result<ConvergeOutcome>.Ok(ConvergeOutcome.Gone);
                }

                var converged = await ConvergeAsync(agent, site, account, settings, webServer, cancellationToken);

                return Result<ConvergeOutcome>.Ok(converged ? ConvergeOutcome.Converged : ConvergeOutcome.Refused);
            },
            cancellationToken);
        if (!guarded.IsSuccess)
        {
            return SiteBusyFailure.Is(guarded.Error) ? ConvergeOutcome.Busy : ConvergeOutcome.Refused;
        }

        return guarded.Value;
    }

    /// <summary>Asks the agent to converge one site, turning anything it throws into a failure.</summary>
    /// <param name="agent">The agent's site operations.</param>
    /// <param name="site">The stored PHP site.</param>
    /// <param name="account">The site's owning account.</param>
    /// <param name="settings">The settings the account's pools carry, cut to its plan's bounds.</param>
    /// <param name="webServer">The installed site web server the vhost is rendered for.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>True when the agent answered success.</returns>
    /// <remarks>
    /// The call is the ordinary version change with the version the row already stores, so the
    /// descriptor is built from the row by the same mapper every handler uses (the vhost keeps its
    /// aliases and its TLS block). The previous pool is never removed: nothing is being left behind.
    /// </remarks>
    private async Task<bool> ConvergeAsync(
        IAgentSitesClient agent,
        Site site,
        AccountSnapshot account,
        IReadOnlyList<PhpSettingDto> settings,
        SiteWebServer webServer,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await agent.ChangePhpVersionAsync(
                account.Username,
                site.Domain,
                site.PhpVersion,
                SiteDescriptorMapper.From(site, webServer, account.PhpLimits),
                (uint)account.MaxPhpWorkersPerPool,
                settings,
                removePreviousPool: false,
                cancellationToken);
            if (result.IsSuccess)
            {
                return true;
            }

            LogSiteFailed(_logger, site.Domain, result.Error!.Code, null);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSiteFailed(_logger, site.Domain, exception.GetType().Name, exception);
            return false;
        }
    }

    /// <summary>Runs one pass, turning anything it throws into a failed attempt.</summary>
    /// <param name="attempt">Which attempt this is, so the log line names it.</param>
    /// <param name="stoppingToken">Cancelled when the host is shutting down.</param>
    /// <returns>True when the pass left no site failed.</returns>
    /// <remarks>
    /// A database that is not reachable yet arrives as an exception. Letting it escape
    /// <see cref="ExecuteAsync"/> would stop the service for the life of the process.
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
