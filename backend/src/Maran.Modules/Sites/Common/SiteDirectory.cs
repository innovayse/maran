using Maran.Modules.Sites.Domain;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Common;

/// <summary>
/// This module's implementation of <see cref="ISiteDirectory"/> — the only window another module has
/// onto the <c>sites</c> schema, and the only hand on the <see cref="Site.HasCertificate"/> switch.
/// </summary>
/// <remarks>
/// The read is left to <see cref="SitesDbContext"/>'s global filter rather than repeating a
/// <c>Where</c> clause, so the scope another module gets is the same scope this module gets, from
/// the same code. The unscoped read is the exception the interface documents, and it says
/// <c>IgnoreQueryFilters</c> out loud rather than obtaining the row some quieter way.
/// </remarks>
public sealed class SiteDirectory : ISiteDirectory
{
    /// <summary>The Sites module's database context, carrying the caller's tenant scope.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Creates the directory over this module's context.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    public SiteDirectory(SitesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<SiteSnapshot?> FindByDomainAsync(string domain, CancellationToken cancellationToken)
    {
        var site = await _dbContext.Sites
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Domain == domain, cancellationToken);

        return site is null ? null : ToSnapshot(site);
    }

    /// <inheritdoc />
    public async Task<SiteSnapshot?> FindByIdUnscopedAsync(Guid siteId, CancellationToken cancellationToken)
    {
        var site = await _dbContext.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == siteId, cancellationToken);

        return site is null ? null : ToSnapshot(site);
    }

    /// <inheritdoc />
    public async Task<bool> AttachCertificateAsync(Guid siteId, CancellationToken cancellationToken)
    {
        return await SetCertificateAsync(siteId, attached: true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> DetachCertificateAsync(Guid siteId, CancellationToken cancellationToken)
    {
        return await SetCertificateAsync(siteId, attached: false, cancellationToken);
    }

    /// <summary>Projects a stored row onto the cross-module snapshot.</summary>
    /// <param name="site">The row that defines the site.</param>
    /// <returns>The snapshot carrying that row's own facts.</returns>
    private static SiteSnapshot ToSnapshot(Site site)
    {
        return new SiteSnapshot(
            site.Id,
            site.AccountId,
            site.Domain,
            site.Aliases,
            ToSdkBackend(site.BackendType),
            site.PhpVersion,
            site.ProxyUpstream,
            site.HasCertificate);
    }

    /// <summary>Maps this module's backend enum onto the Sdk's cross-module one.</summary>
    /// <param name="backendType">The stored backend type.</param>
    /// <returns>The Sdk's matching value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown for a value the mapping does not know. This is a bug, not a domain failure: both enums
    /// are closed, so an unmapped value means the two were changed apart. Failing loudly beats
    /// defaulting to <see cref="SiteBackend.Static"/>, which would hand another module a description
    /// under which a PHP site's vhost re-renders as a static one.
    /// </exception>
    private static SiteBackend ToSdkBackend(SiteBackendType backendType)
    {
        return backendType switch
        {
            SiteBackendType.Static => SiteBackend.Static,
            SiteBackendType.Php => SiteBackend.Php,
            SiteBackendType.ReverseProxy => SiteBackend.ReverseProxy,
            _ => throw new ArgumentOutOfRangeException(nameof(backendType), backendType, "Unmapped site backend type."),
        };
    }

    /// <summary>Flips one site's certificate flag through the entity's own methods.</summary>
    /// <param name="siteId">The site to update.</param>
    /// <param name="attached">Whether a certificate is now installed.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns><c>true</c> when a row was updated; <c>false</c> when no such site exists.</returns>
    private async Task<bool> SetCertificateAsync(Guid siteId, bool attached, CancellationToken cancellationToken)
    {
        var site = await _dbContext.Sites
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == siteId, cancellationToken);
        if (site is null)
        {
            return false;
        }

        if (attached)
        {
            site.AttachCertificate();
        }
        else
        {
            site.DetachCertificate();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
