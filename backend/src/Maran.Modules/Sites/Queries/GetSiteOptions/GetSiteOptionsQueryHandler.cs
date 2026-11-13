using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Mappers;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Resources;
using Maran.Modules.Sites.Services;
using Maran.Sdk.Interfaces;

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

    /// <summary>The owning account, whose plan caps the site's body size.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">The window onto the owning account's plan.</param>
    /// <param name="siteNames">Labels the closed sets for the request's culture.</param>
    public GetSiteOptionsQueryHandler(SitesDbContext dbContext, IAccountDirectory accounts, SiteDisplayNames siteNames)
    {
        _accounts = accounts;
        _dbContext = dbContext;
        _siteNames = siteNames;
    }

    /// <summary>Returns the site's options, or <c>SiteNotFound</c>.</summary>
    /// <param name="query">Which site to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The options view, or <c>SiteNotFound</c> or <c>AccountNotFound</c>.</returns>
    public async Task<Result<SiteOptionsDto>> HandleAsync(GetSiteOptionsQuery query, CancellationToken cancellationToken)
    {
        var site = await _dbContext.Sites
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == query.SiteId, cancellationToken);
        if (site is null)
        {
            return Result<SiteOptionsDto>.Fail(Error.Of(nameof(ErrorMessages.SiteNotFound), ErrorType.NotFound));
        }

        var account = await _accounts.FindAsync(site.AccountId, cancellationToken);
        if (account is null)
        {
            return Result<SiteOptionsDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        return Result<SiteOptionsDto>.Ok(SiteOptionsMapper.From(site, _siteNames, account.PhpLimits));
    }
}
