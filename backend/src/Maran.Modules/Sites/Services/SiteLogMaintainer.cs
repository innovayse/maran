using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// Runs the log maintenance of one site: read what its access log counted, and only then remove the
/// archives beyond its retention (issue #152).
/// </summary>
/// <remarks>
/// <para>
/// <b>The order is the point.</b> The cursor points into the live file or the previous rotation, and a
/// retention is never below one, so a prune cannot remove a file the cursor needs. That holds because the
/// read comes first: a pass that could not read the log (the agent is busy or down) does not prune, so a
/// failed read can never be followed by the removal of the very archive it was about to need.
/// </para>
/// <para>
/// Both callers go through this one method: the hourly pass over every site, and the message published
/// when a customer changes one site's retention. <see cref="SiteLogMaintenanceGate"/> admits one site at a
/// time between them.
/// </para>
/// <para>
/// A failure is logged and answered, never thrown: one broken account must not stall the sites after it.
/// </para>
/// </remarks>
public sealed class SiteLogMaintainer
{
    /// <summary>Pre-compiled log delegate for a site whose owning account is gone.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogAccountMissing =
        LoggerMessage.Define<Guid>(
            LogLevel.Warning,
            new EventId(1, nameof(SiteLogMaintainer)),
            "Skipped log maintenance of site {SiteId}: its owning account does not exist");

    /// <summary>Pre-compiled log delegate for a read the agent refused.</summary>
    private static readonly Action<ILogger, string, string, Exception?> LogReadFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(2, nameof(SiteLogMaintainer)),
            "Could not read the access log of {Domain}: {Code}; its archives are left alone this pass");

    /// <summary>Pre-compiled log delegate for a prune the agent refused.</summary>
    private static readonly Action<ILogger, string, string, Exception?> LogPruneFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(3, nameof(SiteLogMaintainer)),
            "Could not prune the rotated logs of {Domain}: {Code}");

    /// <summary>Pre-compiled log delegate for a site whose maintenance threw.</summary>
    private static readonly Action<ILogger, Guid, Exception?> LogSiteFailed =
        LoggerMessage.Define<Guid>(
            LogLevel.Error,
            new EventId(4, nameof(SiteLogMaintainer)),
            "Log maintenance of site {SiteId} failed; the next pass tries again");

    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Answers the system user name an agent call is addressed by.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Reads the access log and stores the counts.</summary>
    private readonly SiteTrafficIngestor _ingestor;

    /// <summary>Removes the archives beyond the retention.</summary>
    private readonly SiteLogPruner _pruner;

    /// <summary>Admits one site's maintenance at a time.</summary>
    private readonly SiteLogMaintenanceGate _gate;

    /// <summary>Where a skipped or failed site is reported.</summary>
    private readonly ILogger<SiteLogMaintainer> _logger;

    /// <summary>Creates the maintainer.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">Answers the system user name an agent call is addressed by.</param>
    /// <param name="ingestor">Reads the access log and stores the counts.</param>
    /// <param name="pruner">Removes the archives beyond the retention.</param>
    /// <param name="gate">Admits one site's maintenance at a time.</param>
    /// <param name="logger">Where a skipped or failed site is reported.</param>
    public SiteLogMaintainer(
        SitesDbContext dbContext,
        IAccountDirectory accounts,
        SiteTrafficIngestor ingestor,
        SiteLogPruner pruner,
        SiteLogMaintenanceGate gate,
        ILogger<SiteLogMaintainer> logger)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _ingestor = ingestor;
        _pruner = pruner;
        _gate = gate;
        _logger = logger;
    }

    /// <summary>Reads, then prunes, one site's logs.</summary>
    /// <param name="siteId">The site, by id; one that no longer exists is skipped without a complaint.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>
    /// True when the site needed nothing more or both steps succeeded; false when a step was refused or
    /// threw, which has already been logged.
    /// </returns>
    public async Task<bool> MaintainAsync(Guid siteId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // A context shared across a whole pass would keep every site it ever loaded; clearing it
            // makes each site start from what is stored, which is also what a cursor must be read as.
            _dbContext.ChangeTracker.Clear();

            return await MaintainLoadedAsync(siteId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogSiteFailed(_logger, siteId, exception);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Loads the site and its account and runs the two steps in order.</summary>
    /// <param name="siteId">The site, by id.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>See <see cref="MaintainAsync"/>.</returns>
    private async Task<bool> MaintainLoadedAsync(Guid siteId, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // unattended pass: no principal, so the filter would show no site at all
        var site = await _dbContext.Sites
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(stored => stored.Id == siteId, cancellationToken);
#pragma warning restore RS0030
        if (site is null)
        {
            return true;
        }

        var account = await _accounts.FindSystemUserAsync(site.AccountId, cancellationToken);
        if (account is null)
        {
            LogAccountMissing(_logger, siteId, null);
            return true;
        }

        var read = await _ingestor.IngestAsync(site, account.Username, cancellationToken);
        if (!read.IsSuccess)
        {
            LogReadFailed(_logger, site.Domain, read.Error!.Code, null);
            return false;
        }

        var pruned = await _pruner.PruneAsync(site, account.Username, cancellationToken);
        if (!pruned.IsSuccess)
        {
            LogPruneFailed(_logger, site.Domain, pruned.Error!.Code, null);
            return false;
        }

        return true;
    }
}
