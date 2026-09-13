using Maran.Sdk.Contracts;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Reads the small set of site facts other modules need (<see cref="SiteSnapshot"/>), and is the one
/// way another module may record that a site's certificate came or went. The contract lives in the
/// Sdk and its implementation in the module that owns the sites table, the same shape
/// <see cref="IAccountDirectory"/> established — because a module may never reference another module
/// (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// Unlike <see cref="IAccountDirectory"/> this is not read-only, and the exception is narrow and
/// deliberate. A site row carries one fact that only the TLS module can know — whether a certificate
/// is installed — and the vhost renderer must be told it rather than guess it. Rather than let the
/// TLS module reach into another schema, the owning module exposes exactly the two transitions and
/// keeps the write inside itself.
///
/// A cross-module abstraction is precisely where tenant isolation gets bypassed by accident: the
/// global query filter that protects the owning module's own queries does not reach through this
/// interface on its own. Every member below therefore states its own scope, and each is covered by
/// its own isolation test.
/// </remarks>
public interface ISiteDirectory
{
    /// <summary>Reads one site by its primary domain, scoped to what the current user may see.</summary>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The snapshot, or <c>null</c> when no such site exists OR when it belongs to another tenant.
    /// The two cases are deliberately indistinguishable: telling them apart would let a caller
    /// confirm that a domain is hosted here by an account it may not see (rules/security.md — 404,
    /// never 403).
    /// </returns>
    Task<SiteSnapshot?> FindByDomainAsync(string domain, CancellationToken cancellationToken);

    /// <summary>
    /// Reads one site by identity WITHOUT any tenant scope, for unattended work that has no
    /// authenticated caller.
    /// </summary>
    /// <param name="siteId">The site to read.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The snapshot, or <c>null</c> when no such site exists.</returns>
    /// <remarks>
    /// The name says "unscoped" because that is the whole content of the warning: this method must
    /// never be reached from a request path, where it would return another customer's site to
    /// whoever asked. Its one legitimate caller is certificate renewal, which runs on a schedule for
    /// every account on the server and is therefore not acting for any of them. It takes an identity
    /// the caller already holds from its OWN tenant-scoped row rather than a domain, so it cannot be
    /// used to enumerate or probe: a caller must already know a site id to learn anything.
    /// </remarks>
    Task<SiteSnapshot?> FindByIdUnscopedAsync(Guid siteId, CancellationToken cancellationToken);

    /// <summary>Records that a TLS certificate is now installed for a site.</summary>
    /// <param name="siteId">The site whose vhost now has a certificate.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns><c>true</c> when a row was updated; <c>false</c> when no such site exists.</returns>
    /// <remarks>Unscoped for renewal's sake, and safe for it: it changes one derived flag on a site
    /// the caller already identified, and reveals nothing — the answer for a site that exists and one
    /// that does not differ only in a boolean the caller supplied the id for.</remarks>
    Task<bool> AttachCertificateAsync(Guid siteId, CancellationToken cancellationToken);

    /// <summary>Records that a site no longer has a TLS certificate.</summary>
    /// <param name="siteId">The site whose certificate was removed.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns><c>true</c> when a row was updated; <c>false</c> when no such site exists.</returns>
    Task<bool> DetachCertificateAsync(Guid siteId, CancellationToken cancellationToken);
}
