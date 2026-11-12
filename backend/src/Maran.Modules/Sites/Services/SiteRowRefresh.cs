using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Persistence;

namespace Maran.Modules.Sites.Services;

/// <summary>Reads a site again, whole, once a job holds that site's lock.</summary>
/// <remarks>
/// A job lists an account's sites first and visits them one by one, so by the time it holds a site's lock
/// the row it listed may be stale: another operation may have changed its status or version, replaced its
/// options, or deleted it. What the job acts on is what the database says now, read while no other
/// operation can change it.
/// <para>
/// The row is QUERIED again, with the same auto-included graph every handler reads (the options and their
/// redirects, credentials and IP rules), and not reloaded in place: a reload refreshes the row's own columns
/// and leaves the separate options entities exactly as the listing loaded them, so a vhost rendered from it
/// would silently drop the access rules a customer saved while the job waited. The result is not tracked, so
/// no earlier copy of the graph can be mistaken for it.
/// </para>
/// </remarks>
internal static class SiteRowRefresh
{
    /// <summary>Reads one site with everything a vhost is rendered from.</summary>
    /// <param name="dbContext">The Sites module's context.</param>
    /// <param name="siteId">The site to read.</param>
    /// <param name="accountId">The account the job is acting for; a site of another account is never returned.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row as stored now, or null when it was deleted or belongs to another account.</returns>
    public static async Task<Site?> ReadAsync(
        SitesDbContext dbContext,
        Guid siteId,
        Guid accountId,
        CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // an account job has no principal; the tenant boundary is kept by the account id in the predicate
        return await dbContext.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(site => site.Id == siteId && site.AccountId == accountId, cancellationToken);
#pragma warning restore RS0030
    }
}
