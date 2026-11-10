using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Sites.Services;

/// <summary>
/// This module's implementation of <see cref="ISiteRuntimeUsage"/>: which sites an installed app is still
/// running, so the app store can refuse to uninstall it from under them (issue #517).
/// </summary>
/// <remarks>
/// Server-wide on purpose, so every read says <c>IgnoreQueryFilters</c> out loud: the question is "does ANY
/// site, of any account, use this", and a tenant-filtered answer would let an administrator remove a
/// runtime another customer's site depends on. It answers primary domains only, the narrowest value that
/// fits the refusal's list, and it is reached only from the administrator-only uninstall.
/// </remarks>
public sealed class SiteRuntimeUsage : ISiteRuntimeUsage
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Creates the usage reader over this module's context.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    public SiteRuntimeUsage(SitesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListDomainsOnWebServerAsync(CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the uninstall guard asks about every account's sites, not the caller's
        return await _dbContext.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(site => site.Domain)
            .Select(site => site.Domain)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListDomainsOnPhpAsync(string version, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the uninstall guard asks about every account's sites, not the caller's
        return await _dbContext.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(site => site.BackendType == SiteBackendType.Php && site.PhpVersion == version)
            .OrderBy(site => site.Domain)
            .Select(site => site.Domain)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListDomainsOnNodeAsync(string version, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the uninstall guard asks about every account's sites, not the caller's
        return await _dbContext.Sites
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(site => site.BackendType == SiteBackendType.Node && site.NodeVersion == version)
            .OrderBy(site => site.Domain)
            .Select(site => site.Domain)
            .ToListAsync(cancellationToken);
#pragma warning restore RS0030
    }
}
