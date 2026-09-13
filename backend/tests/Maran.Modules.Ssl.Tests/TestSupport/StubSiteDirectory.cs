using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Tests.TestSupport;

/// <summary>
/// An <see cref="ISiteDirectory"/> double holding a fixed set of snapshots, and recording every
/// certificate flag it was asked to flip.
/// </summary>
/// <remarks>
/// Answers null for anything it was not given, which is how the real implementation answers for a
/// site in another tenant — so a test can drive the 404 path without standing up the Sites module.
/// The attach and detach lists are what let a test assert the write path the brief calls for: a
/// certificate installed must flip the site's flag, and a certificate removed must clear it.
/// </remarks>
public sealed class StubSiteDirectory : ISiteDirectory
{
    /// <summary>What this directory knows, keyed by domain.</summary>
    private readonly Dictionary<string, SiteSnapshot> _byDomain;

    /// <summary>Site ids the caller asked to mark as carrying a certificate, in order.</summary>
    public List<Guid> Attached { get; } = [];

    /// <summary>Site ids the caller asked to clear, in order.</summary>
    public List<Guid> Detached { get; } = [];

    /// <summary>Domains this directory was asked about through the tenant-scoped lookup, in order.</summary>
    public List<string> DomainLookups { get; } = [];

    /// <summary>Creates a directory knowing the given sites.</summary>
    /// <param name="sites">The sites this directory can answer for.</param>
    public StubSiteDirectory(params SiteSnapshot[] sites)
    {
        _byDomain = sites.ToDictionary(site =>
        {
            return site.Domain;
        }, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public Task<SiteSnapshot?> FindByDomainAsync(string domain, CancellationToken cancellationToken)
    {
        DomainLookups.Add(domain);
        return Task.FromResult(_byDomain.GetValueOrDefault(domain));
    }

    /// <inheritdoc />
    public Task<SiteSnapshot?> FindByIdUnscopedAsync(Guid siteId, CancellationToken cancellationToken)
    {
        return Task.FromResult(_byDomain.Values.FirstOrDefault(site =>
        {
            return site.Id == siteId;
        }));
    }

    /// <inheritdoc />
    public Task<bool> AttachCertificateAsync(Guid siteId, CancellationToken cancellationToken)
    {
        Attached.Add(siteId);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DetachCertificateAsync(Guid siteId, CancellationToken cancellationToken)
    {
        Detached.Add(siteId);
        return Task.FromResult(true);
    }
}
