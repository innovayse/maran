using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ftp.Tests.TestSupport;

/// <summary>
/// An <see cref="ISiteDirectory"/> that serves exactly the domains a test names, so the enable
/// path's "is this a site this panel serves" question has a decidable answer with no Sites module in
/// the process.
/// </summary>
public sealed class StubSiteDirectory : ISiteDirectory
{
    /// <summary>The domains this panel is pretending to serve.</summary>
    private readonly HashSet<string> _served = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Declares that the panel serves a site for a domain.</summary>
    /// <param name="domain">The domain to serve.</param>
    public void Serve(string domain)
    {
        _served.Add(domain);
    }

    /// <inheritdoc />
    public Task<SiteSnapshot?> FindByDomainAsync(string domain, CancellationToken cancellationToken)
    {
        if (!_served.Contains(domain))
        {
            return Task.FromResult<SiteSnapshot?>(null);
        }

        return Task.FromResult<SiteSnapshot?>(new SiteSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            domain,
            [],
            SiteBackend.Php,
            "8.3",
            string.Empty,
            HasCertificate: true));
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
