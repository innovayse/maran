using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;

namespace Maran.Modules.Sites.Queries.GetSiteOptions;

/// <summary>Handles <see cref="GetSiteOptionsQuery"/> by reading one site's options within the caller's tenant scope.</summary>
/// <remarks>
/// Another tenant's site is not found rather than forbidden, for the reason
/// <see cref="GetSite.GetSiteQueryHandler"/> gives: the context's query filter means the row is not
/// there, so there is nothing whose existence a probe could confirm. A site that has never had options
/// set answers with the empty set, not with "not found": the site exists, and "nothing configured" is
/// the state every site starts in.
/// </remarks>
public sealed class GetSiteOptionsQueryHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Labels the closed sets in the request's culture.</summary>
    private readonly SiteDisplayNames _siteNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="siteNames">Labels the closed sets for the request's culture.</param>
    public GetSiteOptionsQueryHandler(SitesDbContext dbContext, SiteDisplayNames siteNames)
    {
        _dbContext = dbContext;
        _siteNames = siteNames;
    }

    /// <summary>Returns the site's options, or <c>SiteNotFound</c>.</summary>
    /// <param name="query">Which site to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The options view, or <c>SiteNotFound</c>.</returns>
    public async Task<Result<SiteOptionsDto>> HandleAsync(GetSiteOptionsQuery query, CancellationToken cancellationToken)
    {
        var site = await _dbContext.Sites
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.SiteId, cancellationToken);
        if (site is null)
        {
            return Result<SiteOptionsDto>.Fail(Error.Of(nameof(ErrorMessages.SiteNotFound), ErrorType.NotFound));
        }

        return Result<SiteOptionsDto>.Ok(SiteOptionsMapper.From(site, _siteNames));
    }
}
