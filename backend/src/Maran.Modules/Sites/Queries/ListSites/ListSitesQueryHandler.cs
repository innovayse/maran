using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Persistence;

namespace Maran.Modules.Sites.Queries.ListSites;

/// <summary>Handles <see cref="ListSitesQuery"/> by reading <c>sites.Sites</c> within the caller's tenant scope.</summary>
/// <remarks>
/// There is no <c>Where</c> clause on the account here, and there deliberately is not one: the
/// context's global query filter supplies it, so this handler could not leak another tenant's rows
/// even if it were rewritten carelessly (spec §8).
/// </remarks>
public sealed class ListSitesQueryHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    public ListSitesQueryHandler(SitesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the caller's sites, ordered by creation time.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the sites; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<SiteDto>>> HandleAsync(
        ListSitesQuery query,
        CancellationToken cancellationToken)
    {
        var sites = await _dbContext.Sites
            .AsNoTracking()
            .OrderBy(site => site.CreatedAt)
            .Select(site => new SiteDto(
                site.Id, site.AccountId, site.Domain, site.BackendType, site.PhpVersion, site.Status, site.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SiteDto>>.Ok(sites);
    }
}
