using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Backups.Tests.TestSupport;

/// <summary>
/// An <see cref="ISiteDirectory"/> double holding the sites a test says the panel hosts, each with the
/// account that owns it, so a restore narrowed to one site has a decidable answer with no Sites module
/// in the process.
/// </summary>
/// <remarks>
/// Answers <c>null</c> for a domain it was not given, which is how the real implementation answers a
/// site that does not exist and one in another tenant alike. It deliberately also serves a site of an
/// account other than the one a test restores, because that is the case the handler must refuse and a
/// double that could only hold the right owner could never exhibit it.
/// </remarks>
public sealed class StubSiteDirectory : ISiteDirectory
{
    /// <summary>The sites this directory knows, keyed by domain.</summary>
    private readonly Dictionary<string, SiteSnapshot> _sites = new(StringComparer.Ordinal);

    /// <summary>Declares that the panel hosts a site for a domain on an account.</summary>
    /// <param name="accountId">The account that owns the site.</param>
    /// <param name="domain">The site's primary domain.</param>
    public void Host(Guid accountId, string domain)
    {
        _sites[domain] = new SiteSnapshot(
            Guid.NewGuid(),
            accountId,
            domain,
            [],
            SiteBackend.Php,
            "8.3",
            string.Empty,
            HasCertificate: false,
            SiteOptionsSnapshot.None);
    }

    /// <inheritdoc />
    public Task<SiteSnapshot?> FindByDomainAsync(string domain, CancellationToken cancellationToken)
    {
        return Task.FromResult(_sites.GetValueOrDefault(domain));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListDomainsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return Task.FromResult<IReadOnlyList<string>>(
            _sites.Values
                .Where(site => { return site.AccountId == accountId; })
                .Select(site => { return site.Domain; })
                .Order(StringComparer.Ordinal)
                .ToList());
    }

    /// <inheritdoc />
    public Task<SiteSnapshot?> FindByIdUnscopedAsync(Guid siteId, CancellationToken cancellationToken)
    {
        return Task.FromResult<SiteSnapshot?>(null);
    }

    /// <inheritdoc />
    public Task<bool> AttachCertificateAsync(Guid siteId, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> DetachCertificateAsync(Guid siteId, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }
}
