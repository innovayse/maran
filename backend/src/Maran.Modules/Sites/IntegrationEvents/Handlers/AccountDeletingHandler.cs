using Maran.Agent.Client.Interfaces;
using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Events;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.IntegrationEvents.Handlers;

/// <summary>
/// Takes this module's sites off the host and out of the panel for an account that is about to be
/// deleted (<see cref="AccountDeleting"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> It did not, and the cost was measured rather than imagined: a live
/// browser run deleted an account that owned a site, and the deletion reported COMPLETED while the
/// <c>Site</c> row, the nginx vhost and the account's private key stayed exactly where they were.
/// The panel's own sites screen then listed an ENABLED site belonging to an account that did not
/// exist, whose document root did not exist either. <see cref="AccountDeleting"/>'s own remarks name
/// that outcome as the thing the cascade exists to prevent; two modules simply never subscribed.
/// </para>
/// <para>
/// <b>This one removes the HOST's files as well as the rows, and that is not a departure.</b> The
/// Databases and Sftp handlers remove rows only because the agent's own account deletion already
/// drops the databases and revokes the logins — it asks MySQL and the password database what they
/// hold. Nothing asks nginx. The agent's account deletion removes the php-fpm pools, the system user
/// and the home directory, and it has never touched the vhost include directory or the certificate
/// store, both of which live under <c>/etc/maran</c> and outlive the account entirely. So the vhost
/// is removed here, through the same rpc a single site deletion uses, or it is not removed at all.
/// </para>
/// <para>
/// <b>What goes with the vhost.</b> The agent's site deletion purges the domain's certificate
/// material immediately after removing the vhost, which is what takes <c>privkey.pem</c> off the
/// disk. That ordering is the agent's and it is deliberate — unlinking material the running
/// configuration still names makes the next <c>nginx -t</c> fail, possibly for an unrelated site
/// minutes later — and it is why the Ssl module's own subscriber removes rows and nothing else.
/// Neither module reaches into the other's schema; they simply agree that the material belongs to
/// the site it was installed for.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the deletion with the account intact — and this handler runs BEFORE the agent
/// removes the system user, which is what makes the vhost removal safe: a php-fpm pool retired while
/// its account still resolves validates cleanly, and one retired afterwards makes <c>php-fpm -t</c>
/// refuse for every tenant on the box.
/// </para>
/// </remarks>
public sealed class AccountDeletingHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The agent, which owns the vhost include directory.</summary>
    private readonly IAgentSitesClient _agent;

    /// <summary>Names the installed site web server, which every vhost this handler touches belongs to.</summary>
    private readonly SiteStackGate _stack;

    /// <summary>Serialises the operations that change one site's host state.</summary>
    private readonly ISiteOperationLock _locks;

    /// <summary>The panel's clock, which bounds how long the cascade waits for busy sites.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="agent">The agent client that removes each vhost.</param>
    /// <param name="stack">Names the installed site web server the vhosts belong to.</param>
    /// <param name="locks">Serialises the operations that change one site's host state.</param>
    /// <param name="clock">The panel's clock, which bounds how long the cascade waits for busy sites.</param>
    public AccountDeletingHandler(
        SitesDbContext dbContext, IAgentSitesClient agent, SiteStackGate stack, ISiteOperationLock locks, IClock clock)
    {
        _clock = clock;
        _locks = locks;
        _dbContext = dbContext;
        _agent = agent;
        _stack = stack;
    }

    /// <summary>Removes every site the account owns, from the host first and then from the panel.</summary>
    /// <remarks>
    /// The tenant query filter is deliberately bypassed, for the reason the Databases module's
    /// handler sets out: the filter governs what a REQUEST may see, and this is the removal of the
    /// account the rows belong to, authorised before the event was published. A filtered query here
    /// would answer correctly while account deletion stays an administrator's operation and would
    /// leave a customer's sites behind silently the day it does not.
    /// </remarks>
    /// <param name="message">The account about to be deleted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="InvalidOperationException">
    /// The agent refused to remove a vhost. Thrown rather than returned, because a subscriber's only
    /// way to abort the deletion is to fail — and a site whose vhost is still served by an account
    /// that no longer exists is precisely the orphan the cascade must refuse to create. The agent's
    /// own text is carried in the exception, which the Accounts handler logs and never answers a
    /// customer with (rules/security.md item 8).
    /// </exception>
    public async Task HandleAsync(AccountDeleting message, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the account is being deleted, so its rows must be found whoever asked for the deletion
        var owned = await _dbContext.Sites
            .IgnoreQueryFilters()
            .Include(site => site.Hostnames)
            .Where(site => site.AccountId == message.AccountId)
            .OrderBy(site => site.Domain)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030

        // The account's PHP settings go with it. Read here and removed beside the sites, so a deletion
        // that is abandoned because the agent refused a vhost has taken nothing from the panel.
#pragma warning disable RS0030 // the account is being deleted, so its rows must be found whoever asked for the deletion
        var settings = await _dbContext.PhpSettings
            .IgnoreQueryFilters()
            .Where(setting => setting.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
        if (owned.Count == 0)
        {
            if (settings.Count > 0)
            {
                _dbContext.PhpSettings.RemoveRange(settings);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        // Every version the account's sites run is retired exactly once, on the last site holding
        // it. Retiring it on the first would ask the agent to remove a pool two of the account's own
        // sites still name — and the removal protocol validates AFTER unlinking, so php-fpm would
        // refuse and the pool would be put back, failing a deletion that had nothing wrong with it.
        var webServer = await _stack.RequireWebServerAsync(cancellationToken);
        var removedIds = new HashSet<Guid>();
        var vanished = new HashSet<Guid>();

        // Nothing is deleted from the panel unless every vhost went; the deletion is then retried whole. The
        // row is read again under each lock: a site another request deleted since the listing has no vhost
        // left to remove and no row left to delete here, so it drops out of this pass.
        await SiteCascade.RunAsync(
            owned,
            _locks,
            _clock,
            message.AccountId,
            "removed",
            (siteId, budget) =>
            {
                return RemoveVhostAsync(siteId, message.AccountId, removedIds, message.Username, webServer, vanished, budget, cancellationToken);
            },
            cancellationToken);

        owned.RemoveAll(site =>
        {
            return vanished.Contains(site.Id);
        });

        // The hostname claims come with the sites through the tracked graph, so the names the
        // account held are freed rather than left claiming a site that no longer exists.
        _dbContext.Sites.RemoveRange(owned);
        _dbContext.PhpSettings.RemoveRange(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Removes one site's vhost, which the caller holds, unless the site has been deleted since the listing.</summary>
    /// <param name="siteId">The listed site.</param>
    /// <param name="accountId">The account being deleted.</param>
    /// <param name="removedIds">The sites whose vhost an earlier step of this cascade already removed.</param>
    /// <param name="username">The owning account's system user name.</param>
    /// <param name="webServer">The installed site web server.</param>
    /// <param name="vanished">Collects the ids of the sites found already deleted.</param>
    /// <param name="budget">What is left of the cascade's waiting time, spent on the account's pools.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Success, or the agent's refusal.</returns>
    private async Task<Result> RemoveVhostAsync(
        Guid siteId,
        Guid accountId,
        HashSet<Guid> removedIds,
        string username,
        SiteWebServer webServer,
        HashSet<Guid> vanished,
        SiteLockBudget budget,
        CancellationToken cancellationToken)
    {
        var site = await SiteRowRefresh.ReadAsync(_dbContext, siteId, accountId, cancellationToken);
        if (site is null)
        {
            vanished.Add(siteId);
            return Result.Ok();
        }

        // A site that holds no pool asks nothing of the account's pools. One that does decides, and acts on,
        // "the last site on this version" under the account's pool lock, so a version change of another site
        // moving onto the same version cannot lose the pool it has just been given.
        return site.BackendType == SiteBackendType.Php && !string.IsNullOrEmpty(site.PhpVersion)
            ? await PhpPoolGuard.RunAsync(
                _locks,
                site.AccountId,
                budget.NextWait(),
                () =>
                {
                    return RemoveAsync(site, removedIds, username, webServer, cancellationToken);
                },
                cancellationToken)
            : await RemoveAsync(site, removedIds, username, webServer, cancellationToken);
    }

    /// <summary>Asks the agent to remove one site's vhost, retiring the pool of the version it was the last on.</summary>
    /// <param name="site">The site as stored now.</param>
    /// <param name="removedIds">The sites whose vhost an earlier step of this cascade already removed.</param>
    /// <param name="username">The owning account's system user name.</param>
    /// <param name="webServer">The installed site web server.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Success, or the agent's refusal.</returns>
    private async Task<Result> RemoveAsync(
        Site site,
        HashSet<Guid> removedIds,
        string username,
        SiteWebServer webServer,
        CancellationToken cancellationToken)
    {
        var removed = await _agent.DeleteAsync(
            username, site.Domain, await RetiredVersionAsync(site, removedIds, cancellationToken), webServer, cancellationToken);

        // NotFound means the vhost is already gone, which a retry of a half-finished cascade meets for
        // every site the first attempt removed; reading it as a failure would make that retry impossible.
        if (!removed.IsSuccess && removed.Error!.Type != ErrorType.NotFound)
        {
            return Result.Fail(removed.Error!);
        }

        removedIds.Add(site.Id);

        return Result.Ok();
    }

    /// <summary>The php-fpm version this site's removal retires, or the empty string when none.</summary>
    /// <param name="site">The site being removed, as stored now.</param>
    /// <param name="removedIds">The sites whose vhost an earlier step of this cascade already removed.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The version to retire, or the empty string, which the agent reads as "leave the pool".</returns>
    /// <remarks>
    /// Only the LAST site on a version retires it, so the pool outlives every site that still names it. The
    /// answer is read from the rows now, under the account's pool lock, and not from the listing: a site
    /// moved onto this version since the listing still needs the pool. Rows of this cascade stay until it
    /// ends, so the sites it has already removed are excluded by id.
    /// </remarks>
    private async Task<string> RetiredVersionAsync(Site site, HashSet<Guid> removedIds, CancellationToken cancellationToken)
    {
        if (site.BackendType != SiteBackendType.Php || string.IsNullOrEmpty(site.PhpVersion))
        {
            return string.Empty;
        }

#pragma warning disable RS0030 // an account job has no principal; the read is bounded by one account id
        var stillUsed = await _dbContext.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                other => other.AccountId == site.AccountId
                    && other.Id != site.Id
                    && other.PhpVersion == site.PhpVersion
                    && !removedIds.Contains(other.Id),
                cancellationToken);
#pragma warning restore RS0030

        return stillUsed ? string.Empty : site.PhpVersion;
    }
}
