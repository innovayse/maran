using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Accounts.Queries.ListAccountChoices;

/// <summary>Handles <see cref="ListAccountChoicesQuery"/> by reading one page of account names.</summary>
public sealed class ListAccountChoicesQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public ListAccountChoicesQueryHandler(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Searches the account names and answers the requested page with the total that matched.</summary>
    /// <param name="query">The validated picker request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<AccountChoiceDto>>> HandleAsync(
        ListAccountChoicesQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Account> accounts = _dbContext.Accounts.AsNoTracking();

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the
            // wildcards so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            accounts = accounts.Where(account => EF.Functions.ILike(account.Name, pattern, LikePattern.EscapeCharacter));
        }

        var page = await accounts.ToPagedAsync(query, ListAccountChoicesSorts.Map, cancellationToken);

        return Result<PagedResult<AccountChoiceDto>>.Ok(page.Map(account =>
        {
            return new AccountChoiceDto(account.Id, account.Name);
        }));
    }
}
