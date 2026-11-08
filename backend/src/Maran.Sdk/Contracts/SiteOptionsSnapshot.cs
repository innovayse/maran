namespace Maran.Sdk.Contracts;

/// <summary>
/// The per-site options another module needs to re-render a site's vhost without losing them.
/// </summary>
/// <remarks>
/// The reason it exists is the one <see cref="SiteSnapshot"/> gives: installing or removing a
/// certificate REWRITES the vhost, and a rewrite built without these drops the site's redirects, basic
/// authentication, IP rules and request limit on an action that had nothing to do with them. Absent is expressed by
/// absence — a null size, empty lists, no IP rules — and <see cref="None"/> is the options of a site
/// that has none set.
/// </remarks>
/// <param name="CanonicalHost">Which of the apex / "www." pair is canonical.</param>
/// <param name="ClientMaxBodySizeMib">The largest request body in MiB, or null for the web server's default.</param>
/// <param name="StaticCacheDays">How many days browsers may cache static files, or null for no cache headers.</param>
/// <param name="Redirects">The redirect rules, in the order they are rendered.</param>
/// <param name="BasicAuthUsers">The basic-authentication users; empty means no authentication.</param>
/// <param name="IpAccess">The IP rules, or null when the site has none.</param>
/// <param name="RequestRate">The request limit, or null when the site is not limited.</param>
public sealed record SiteOptionsSnapshot(
    SiteCanonicalHost CanonicalHost,
    int? ClientMaxBodySizeMib,
    int? StaticCacheDays,
    IReadOnlyList<SiteRedirectSnapshot> Redirects,
    IReadOnlyList<SiteBasicAuthUserSnapshot> BasicAuthUsers,
    SiteIpAccessSnapshot? IpAccess,
    SiteRequestRateSnapshot? RequestRate)
{
    /// <summary>The options of a site that has none set.</summary>
    public static readonly SiteOptionsSnapshot None = new(SiteCanonicalHost.None, null, null, [], [], null, null);
}
