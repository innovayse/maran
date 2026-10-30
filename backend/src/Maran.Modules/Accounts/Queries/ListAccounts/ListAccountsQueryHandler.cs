using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.SharedKernel.Extensions;
using Maran.SharedKernel.Utilities.Text;

namespace Maran.Modules.Accounts.Queries.ListAccounts;

/// <summary>Handles <see cref="ListAccountsQuery"/> by reading one page of <c>accounts.Accounts</c>.</summary>
public sealed class ListAccountsQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public ListAccountsQueryHandler(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Filters by plan and status, searches the name and the primary domain, and answers the
    /// requested page with the total that matched.
    /// </summary>
    /// <param name="query">The validated list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the page; this operation never fails.</returns>
    public async Task<Result<PagedResult<AccountDto>>> HandleAsync(
        ListAccountsQuery query,
        CancellationToken cancellationToken)
    {
        IQueryable<Account> accounts = _dbContext.Accounts.AsNoTracking();

        if (query.PlanId is { } planId)
        {
            accounts = accounts.Where(account => account.PlanId == planId);
        }

        if (query.Status is { } status)
        {
            accounts = accounts.Where(account => account.Status == status);
        }

        if (!query.Search.IsNone)
        {
            // A captured local, so EF Core sends it as a parameter; LikePattern escapes the
            // wildcards so the term matches literally.
            var pattern = LikePattern.Contains(query.Search.Value);
            accounts = accounts.Where(account =>
                EF.Functions.ILike(account.Name, pattern, LikePattern.EscapeCharacter) || EF.Functions.ILike(account.PrimaryDomain, pattern, LikePattern.EscapeCharacter));
        }

        var page = await accounts.ToPagedAsync(query, ListAccountsSorts.Map, cancellationToken);

        return Result<PagedResult<AccountDto>>.Ok(page.Map(account =>
        {
            return new AccountDto(
                account.Id, account.Name, account.PrimaryDomain, account.PlanId, account.Status, account.CreatedAt);
        }));
    }
}
