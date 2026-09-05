namespace Maran.Modules.Sites.Domain.Entities;

/// <summary>
/// One hostname claimed by one site: the site's primary domain, or one of its aliases (spec §11).
/// </summary>
/// <remarks>
/// A hostname is a claim on a name across the WHOLE server, not within one account, because nginx
/// resolves a request by <c>Host</c> alone and knows nothing about tenants. Two vhosts naming the
/// same hostname are not a duplicate row — they are a takeover: the include is a sorted glob, so
/// the alphabetically first file wins every request for that name, including
/// <c>/.well-known/acme-challenge/</c>, which is enough to obtain a publicly trusted certificate
/// for a domain the requester does not own. <c>nginx -t</c> only warns on a conflicting
/// <c>server_name</c>, so nothing downstream refuses it.
///
/// The name is therefore the primary key of this table, which makes the claim exclusive in the
/// database rather than only in a handler: a pre-check and an insert are not one atomic step, and
/// two simultaneous requests for the same name must not both succeed. This is the same reasoning
/// that puts a unique index on <see cref="Site.Domain"/>, extended to the set the vhost actually
/// answers for — <c>Domain ∪ Aliases</c>.
///
/// Rows are created by <see cref="Site"/>'s constructor and by nothing else, so a site cannot exist
/// without its claims; they are removed with the site by the relationship's cascade.
/// </remarks>
public sealed class SiteHostname
{
    /// <summary>The claimed hostname, lower-cased. Unique across every account on this server.</summary>
    public string Name { get; private set; }

    /// <summary>The site answering for this hostname.</summary>
    public Guid SiteId { get; private set; }

    /// <summary>The site answering for this hostname, as the relationship EF Core cascades on.</summary>
    /// <remarks>
    /// Present so the tenant query filter can reach the owning account through it rather than
    /// storing a second copy of the account id that could disagree with the site's own.
    /// </remarks>
    public Site Site { get; private set; }

    /// <summary>Claims <paramref name="name"/> for <paramref name="site"/>.</summary>
    /// <param name="name">The hostname to claim; stored lower-cased, since <c>Host</c> matching is case-insensitive.</param>
    /// <param name="site">The site answering for it.</param>
    public SiteHostname(string name, Site site)
    {
        Name = name.ToLowerInvariant();
        Site = site;
        SiteId = site.Id;
    }

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    private SiteHostname()
    {
        Name = string.Empty;
        Site = null!;
    }
}
