namespace Maran.Sdk.Contracts;

/// <summary>
/// The facts one module needs about a site owned by another: enough to re-render its vhost, and
/// nothing else. The site-shaped counterpart of <see cref="AccountSnapshot"/>.
/// </summary>
/// <remarks>
/// A snapshot is read on the request path and used immediately; it is never stored. The TLS module
/// needs it because installing a certificate REWRITES the site's vhost, and the agent keeps no
/// record of what the site is — so the panel must hand it every fact the renderer needs or the
/// rewritten vhost silently becomes a different site (a PHP site turned static, a site with aliases
/// that answers for one name).
/// </remarks>
/// <param name="Id">The site's identity.</param>
/// <param name="AccountId">The account that owns the site; the value a tenant-scoped row carries.</param>
/// <param name="Domain">The site's primary domain.</param>
/// <param name="Aliases">Additional hostnames answered by the same vhost.</param>
/// <param name="Backend">Which backend serves the site's content.</param>
/// <param name="PhpVersion">The bound PHP version, or the empty string when the backend is not PHP.</param>
/// <param name="ProxyUpstream">The upstream forwarded to, or the empty string when the backend is not a proxy.</param>
/// <param name="HasCertificate">
/// Whether a TLS certificate is currently installed. Carried so a re-render keeps the site's TLS
/// block; a renderer that guesses drops a live site back to plain HTTP on the next unrelated edit.
/// </param>
public sealed record SiteSnapshot(
    Guid Id,
    Guid AccountId,
    string Domain,
    IReadOnlyList<string> Aliases,
    SiteBackend Backend,
    string PhpVersion,
    string ProxyUpstream,
    bool HasCertificate);
