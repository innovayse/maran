using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Models;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.ListResellers;

/// <summary>Handles <see cref="ListResellersQuery"/> by reading one page of <c>accounts.Resellers</c>.</summary>
public sealed class ListResellersQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Measures what the resellers on the page own.</summary>
    private readonly ResellerUsageReader _usage;

    /// <summary>Names each reseller's state in the response, in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="usage">Measures what the resellers on the page own.</param>
    /// <param name="displayNames">Names each reseller's state in the response, in the request's culture.</param>
    public ListResellersQueryHandler(
        AccountsDbContext dbContext,
        ResellerUsageReader usage,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _usage = usage;
        _displayNames = displayNames;
    }

    /// <summary>
    /// Filters by state, searches the name and the contact address, answers the requested page with the
    /// total that matched, and measures what each reseller on the page owns.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<ResellerDto>>> HandleAsync(
        ListResellersQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Reseller> resellers = _dbContext.Resellers.AsNoTracking();

        if (query.Status is { } status)
        {
            resellers = resellers.Where(reseller => reseller.Status == status);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards so
            // the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            resellers = resellers.Where(reseller =>
                EF.Functions.ILike(reseller.Name, pattern, LikePattern.EscapeCharacter)
                || EF.Functions.ILike(reseller.ContactEmail, pattern, LikePattern.EscapeCharacter));
        }

        var page = await resellers.ToPagedAsync(query, ListResellersSorts.Map, cancellationToken);

        var ids = page.Items
            .Select(reseller =>
            {
                return reseller.Id;
            })
            .ToList();
        var usage = await _usage.ReadManyAsync(ids, cancellationToken);

        return Result<PagedResult<ResellerDto>>.Ok(page.Map(reseller =>
        {
            var owned = usage.GetValueOrDefault(reseller.Id, ResellerUsage.None);
            return ResellerMapper.ToDto(reseller, owned, _displayNames);
        }));
    }
}
