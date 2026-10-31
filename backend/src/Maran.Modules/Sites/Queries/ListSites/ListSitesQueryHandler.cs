using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Entities;
using Maran.Modules.Sites.Persistence;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Sites.Queries.ListSites;

/// <summary>Handles <see cref="ListSitesQuery"/> by reading one page of <c>sites.Sites</c>.</summary>
/// <remarks>
/// <para>
/// The tenant boundary is not any <c>Where</c> clause here: the context's global query filter
/// supplies it, before the count as well as before the page, so a customer's total can never count
/// another tenant's rows (spec §8). Every clause below only narrows that scope.
/// </para>
/// <para>
/// <b>The owner's name belongs to the Accounts module, and this handler never reads its tables.</b>
/// A search on it is answered in two steps through <see cref="IAccountDirectory"/>: the directory
/// returns the ids of every account whose name matches — tenant-scoped and uncapped, so no matching
/// owner is silently left out — and the sites are then filtered by those ids in this module's own
/// query, where Npgsql sends the set as one array parameter. The names printed beside the rows come from the same directory, in one batched lookup for
/// the whole page.
/// </para>
/// </remarks>
public sealed class ListSitesQueryHandler
{
    /// <summary>The Sites module's database context.</summary>
    private readonly SitesDbContext _dbContext;

    /// <summary>The window onto the Accounts module's account names.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Creates the handler with the module's own database context and the account directory.</summary>
    /// <param name="dbContext">The Sites module's database context.</param>
    /// <param name="accounts">The window onto the Accounts module's account names.</param>
    public ListSitesQueryHandler(SitesDbContext dbContext, IAccountDirectory accounts)
    {
        _dbContext = dbContext;
        _accounts = accounts;
    }

    /// <summary>
    /// Filters by account, backend, PHP version and status, searches the domain and the owner's name,
    /// and answers the requested page with the total that matched and each row's owner name.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<SiteDto>>> HandleAsync(
        ListSitesQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Site> sites = _dbContext.Sites.AsNoTracking();

        if (query.AccountId is { } accountId)
        {
            sites = sites.Where(site => site.AccountId == accountId);
        }

        if (query.BackendType is { } backendType)
        {
            sites = sites.Where(site => site.BackendType == backendType);
        }

        if (!query.PhpVersion.IsNone)
        {
            var phpVersion = query.PhpVersion.Value;
            sites = sites.Where(site => site.PhpVersion == phpVersion);
        }

        if (query.Status is { } status)
        {
            sites = sites.Where(site => site.Status == status);
        }

        if (!query.Search.IsNone)
        {
            // Captured locals, so EF Core sends both as parameters; LikePattern escapes the
            // wildcards so the term matches literally.
            var term = query.Search.Value;
            var pattern = LikePattern.Contains(term);
            var owners = await _accounts.FindIdsByNameAsync(term, cancellationToken);
            sites = sites.Where(site => EF.Functions.ILike(site.Domain, pattern, LikePattern.EscapeCharacter) || owners.Contains(site.AccountId));
        }

        var page = await sites.ToPagedAsync(query, ListSitesSorts.Map, cancellationToken);

        var ownerIds = page.Items
            .Select(site =>
            {
                return site.AccountId;
            })
            .Distinct()
            .ToList();
        var ownerNames = await _accounts.NamesOfAsync(ownerIds, cancellationToken);

        return Result<PagedResult<SiteDto>>.Ok(page.Map(site =>
        {
            return new SiteDto(
                site.Id,
                site.AccountId,
                ownerNames.GetValueOrDefault(site.AccountId, string.Empty),
                site.Domain,
                site.BackendType,
                site.PhpVersion,
                site.Status,
                site.CreatedAt);
        }));
    }
}
