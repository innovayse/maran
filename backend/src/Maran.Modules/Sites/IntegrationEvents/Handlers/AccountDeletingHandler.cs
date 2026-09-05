using Maran.Agent.Client.Interfaces;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Events;

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

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="agent">The agent client that removes each vhost.</param>
    public AccountDeletingHandler(SitesDbContext dbContext, IAgentSitesClient agent)
    {
        _dbContext = dbContext;
        _agent = agent;
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
        if (owned.Count == 0)
        {
            return;
        }

        // Every version the account's sites run is retired exactly once, on the last site holding
        // it. Retiring it on the first would ask the agent to remove a pool two of the account's own
        // sites still name — and the removal protocol validates AFTER unlinking, so php-fpm would
        // refuse and the pool would be put back, failing a deletion that had nothing wrong with it.
        var retired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var site in owned)
        {
            var removed = await _agent.DeleteAsync(
                message.Username, site.Domain, RetiredVersion(site, owned, retired), cancellationToken);
            if (!removed.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"the vhost of {site.Domain} could not be removed: {removed.Error?.Code}");
            }
        }

        // The hostname claims come with the sites through the tracked graph, so the names the
        // account held are freed rather than left claiming a site that no longer exists.
        _dbContext.Sites.RemoveRange(owned);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The php-fpm version this site's removal retires, or the empty string when none.</summary>
    /// <param name="site">The site being removed.</param>
    /// <param name="owned">Every site the account owns, in the order they are being removed.</param>
    /// <param name="retired">The versions already retired by an earlier site in this cascade.</param>
    /// <returns>The version to retire, or the empty string, which the agent reads as "leave the pool".</returns>
    private static string RetiredVersion(Site site, List<Site> owned, HashSet<string> retired)
    {
        if (site.BackendType != SiteBackendType.Php || string.IsNullOrEmpty(site.PhpVersion))
        {
            return string.Empty;
        }

        // Only the LAST site on a version retires it, so the pool outlives every site that still
        // names it. `retired` then makes the answer idempotent for the degenerate case of two sites
        // that compare equal on the ordering above.
        var lastOnThisVersion = owned.FindLast(other =>
        {
            return other.PhpVersion == site.PhpVersion;
        });

        return lastOnThisVersion == site && retired.Add(site.PhpVersion) ? site.PhpVersion : string.Empty;
    }
}
