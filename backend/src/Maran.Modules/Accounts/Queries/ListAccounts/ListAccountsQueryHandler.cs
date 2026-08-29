using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Persistence;

namespace Maran.Modules.Accounts.Queries.ListAccounts;

/// <summary>Handles <see cref="ListAccountsQuery"/> by reading every account from <c>accounts.Accounts</c>.</summary>
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

    /// <summary>Returns every account as a list-shaped <see cref="AccountDto"/>, ordered by creation time.</summary>
    /// <param name="query">The (parameterless) list request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the accounts; this operation never fails.</returns>
    public async Task<Result<IReadOnlyList<AccountDto>>> Handle(
        ListAccountsQuery query,
        CancellationToken cancellationToken)
    {
        var accounts = await _dbContext.Accounts
            .AsNoTracking()
            .OrderBy(a => a.CreatedAt)
            .Select(a => new AccountDto(a.Id, a.Name, a.PrimaryDomain, a.PlanId, a.Status, a.CreatedAt))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<AccountDto>>.Ok(accounts);
    }
}
