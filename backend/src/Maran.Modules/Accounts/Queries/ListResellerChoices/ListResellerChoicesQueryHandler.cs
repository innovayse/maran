using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Accounts.Queries.ListResellerChoices;

/// <summary>Handles <see cref="ListResellerChoicesQuery"/> by reading one page of reseller names.</summary>
public sealed class ListResellerChoicesQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public ListResellerChoicesQueryHandler(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Searches the reseller names and answers the requested page with the total that matched.</summary>
    /// <param name="query">The validated picker request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<ResellerChoiceDto>>> HandleAsync(
        ListResellerChoicesQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Reseller> resellers = _dbContext.Resellers.AsNoTracking();

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the wildcards so
            // the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            resellers = resellers.Where(reseller =>
                EF.Functions.ILike(reseller.Name, pattern, LikePattern.EscapeCharacter));
        }

        var page = await resellers.ToPagedAsync(query, ListResellerChoicesSorts.Map, cancellationToken);

        return Result<PagedResult<ResellerChoiceDto>>.Ok(page.Map(reseller =>
        {
            return new ResellerChoiceDto(reseller.Id, reseller.Name);
        }));
    }
}
