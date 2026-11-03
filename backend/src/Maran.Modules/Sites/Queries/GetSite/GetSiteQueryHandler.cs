using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;

namespace Maran.Modules.Sites.Queries.GetSite;

/// <summary>Handles <see cref="GetSiteQuery"/> by reading one site within the caller's tenant scope.</summary>
/// <remarks>
/// Another tenant's site is not found rather than forbidden, and that is not a politeness: 403
/// confirms the id names a real site, which turns this endpoint into an oracle for enumerating
/// other customers' sites (rules/testing.md item 3). The distinction is not made by this handler
/// at all — the context's query filter means the row genuinely is not there.
/// </remarks>
public sealed class GetSiteQueryHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Names the site's backend and serving state in the request's culture.</summary>
    private readonly SiteDisplayNames _siteNames;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="siteNames">Names the site's backend and serving state for the request's culture.</param>
    public GetSiteQueryHandler(SitesDbContext dbContext, SiteDisplayNames siteNames)
    {
        _dbContext = dbContext;
        _siteNames = siteNames;
    }

    /// <summary>Returns the site, or <c>SiteNotFound</c>.</summary>
    /// <param name="query">Which site to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The site's detail view, or <c>SiteNotFound</c>.</returns>
    public async Task<Result<SiteDetailDto>> HandleAsync(GetSiteQuery query, CancellationToken cancellationToken)
    {
        var site = await _dbContext.Sites
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Id == query.SiteId, cancellationToken);

        if (site is null)
        {
            return Result<SiteDetailDto>.Fail(Error.Of(nameof(ErrorMessages.SiteNotFound), ErrorType.NotFound));
        }

        return Result<SiteDetailDto>.Ok(new SiteDetailDto(
            site.Id,
            site.AccountId,
            site.Domain,
            site.Aliases,
            site.BackendType,
            _siteNames.Of(site.BackendType),
            site.PhpVersion,
            site.ProxyUpstream,
            site.DocumentRoot,
            site.HasCertificate,
            site.Status,
            _siteNames.Of(site.Status),
            site.CreatedAt));
    }
}
