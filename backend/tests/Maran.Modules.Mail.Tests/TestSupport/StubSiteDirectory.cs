using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>
/// An <see cref="ISiteDirectory"/> answering from the sites a test declares, each owned by a named account.
/// A domain nobody declared answers <c>null</c>, as the real window does for a domain the caller may not see.
/// </summary>
public sealed class StubSiteDirectory : ISiteDirectory
{
    /// <summary>The declared sites, by primary domain.</summary>
    private readonly Dictionary<string, SiteSnapshot> _sites = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Declares a site that serves <paramref name="domain"/> as its primary domain.</summary>
    /// <param name="domain">The primary domain.</param>
    /// <param name="accountId">The account that owns the site.</param>
    /// <returns>The declared site's identity.</returns>
    public Guid Serve(string domain, Guid accountId)
    {
        var siteId = Guid.NewGuid();
        _sites[domain] = new SiteSnapshot(
            siteId,
            accountId,
            domain,
            [],
            SiteBackend.Php,
            "8.3",
            string.Empty,
            HasCertificate: true,
            SiteOptionsSnapshot.None);

        return siteId;
    }

    /// <inheritdoc />
    public Task<SiteSnapshot?> FindByDomainAsync(string domain, CancellationToken cancellationToken)
    {
        return Task.FromResult(_sites.TryGetValue(domain, out var site) ? site : null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListDomainsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    /// <inheritdoc />
    public Task<SiteSnapshot?> FindByIdUnscopedAsync(Guid siteId, CancellationToken cancellationToken)
    {
        return Task.FromResult<SiteSnapshot?>(null);
    }

    /// <inheritdoc />
    public Task<bool> AttachCertificateAsync(Guid siteId, CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DetachCertificateAsync(Guid siteId, CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }
}
