using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;

namespace Maran.Modules.Accounts.Queries.GetAccount;

/// <summary>Handles <see cref="GetAccountQuery"/> by reading one row from <c>accounts.Accounts</c>.</summary>
public sealed class GetAccountQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public GetAccountQueryHandler(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Returns the account, or a typed failure when there is none.</summary>
    /// <param name="query">Which account to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The account as an <see cref="AccountDetailDto"/>, or <c>AccountNotFound</c>.</returns>
    public async Task<Result<AccountDetailDto>> HandleAsync(GetAccountQuery query, CancellationToken cancellationToken)
    {
        var account = await _dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == query.AccountId)
            .Select(a => new AccountDetailDto(a.Id, a.Name, a.PrimaryDomain, a.PlanId, a.Status, a.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return account is null
            ? Result<AccountDetailDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound)))
            : Result<AccountDetailDto>.Ok(account);
    }
}
