using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Sites.Jobs;

/// <summary>
/// Runs <see cref="SiteLogMaintenanceRequested"/>: for every site, read its access log and then prune
/// its archives to its own retention, one site at a time, and finally purge traffic rows older than the
/// panel keeps (issue #152).
/// </summary>
/// <remarks>
/// <para>
/// <b>Per site, in order, and a failing site does not stop the rest.</b> The work for a site is
/// <see cref="SiteLogMaintainer"/>, which reads before it prunes and answers a refusal instead of
/// throwing. The sites are walked one after another because the agent admits only a couple of summarising
/// reads at a time and a pass has no deadline.
/// </para>
/// <para>
/// <b>It reads across accounts on purpose.</b> There is no principal at the hour, so the list of sites
/// lifts the tenant filter, and it reads ids only.
/// </para>
/// <para>
/// Nothing here is journalled except what a prune removed (by <see cref="SiteLogPruner"/>): reading a log
/// is not a decision, and the counts go to the log.
/// </para>
/// </remarks>
public sealed class SiteLogMaintenanceHandler
{
    /// <summary>Pre-compiled log delegate for a completed pass.</summary>
    private static readonly Action<ILogger, int, int, int, Exception?> LogMaintained =
        LoggerMessage.Define<int, int, int>(
            LogLevel.Information,
            new EventId(1, nameof(SiteLogMaintenanceHandler)),
            "Site log maintenance: {Succeeded} sites done, {Failed} refused or failed; purged {Purged} old traffic rows");

    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Maintains one site at a time.</summary>
    private readonly SiteLogMaintainer _maintainer;

    /// <summary>Removes expired traffic rows.</summary>
    private readonly SiteTrafficPurger _purger;

    /// <summary>Where the outcome of each pass is reported.</summary>
    private readonly ILogger<SiteLogMaintenanceHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="maintainer">Maintains one site at a time.</param>
    /// <param name="purger">Removes expired traffic rows.</param>
    /// <param name="logger">Where the outcome of each pass is reported.</param>
    public SiteLogMaintenanceHandler(
        SitesDbContext dbContext,
        SiteLogMaintainer maintainer,
        SiteTrafficPurger purger,
        ILogger<SiteLogMaintenanceHandler> logger)
    {
        _dbContext = dbContext;
        _maintainer = maintainer;
        _purger = purger;
        _logger = logger;
    }

    /// <summary>Runs one pass over every site.</summary>
    /// <param name="message">The scheduled trigger; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancels the pass between sites.</param>
    /// <returns>Resolves when the pass has finished.</returns>
    public async Task HandleAsync(SiteLogMaintenanceRequested message, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // unattended hourly pass: no principal, and every account's sites are maintained
        var siteIds = await _dbContext.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(site => site.CreatedAt)
            .Select(site => site.Id)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        var succeeded = 0;
        var failed = 0;
        foreach (var siteId in siteIds)
        {
            if (await _maintainer.MaintainAsync(siteId, cancellationToken))
            {
                succeeded++;
            }
            else
            {
                failed++;
            }
        }

        var purged = await _purger.PurgeAsync(cancellationToken);

        LogMaintained(_logger, succeeded, failed, purged, null);
    }
}
