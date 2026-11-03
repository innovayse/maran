using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.GetAccount;

/// <summary>
/// Handles <see cref="GetAccountQuery"/> by reading one row from <c>accounts.Accounts</c> together
/// with the plan it is on.
/// </summary>
public sealed class GetAccountQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Names the owning reseller.</summary>
    private readonly ResellerNameDirectory _resellerNames;

    /// <summary>Resolves a shipped plan's name in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="resellerNames">Names the owning reseller.</param>
    /// <param name="displayNames">Resolves a shipped plan's name in the request's culture.</param>
    public GetAccountQueryHandler(
        AccountsDbContext dbContext,
        ResellerNameDirectory resellerNames,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _resellerNames = resellerNames;
        _displayNames = displayNames;
    }

    /// <summary>Returns the account, or a typed failure when there is none.</summary>
    /// <param name="query">Which account to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The account as an <see cref="AccountDetailDto"/>, or <c>AccountNotFound</c> — also for an account
    /// another reseller owns, which the context's filter hides.
    /// </returns>
    public async Task<Result<AccountDetailDto>> HandleAsync(GetAccountQuery query, CancellationToken cancellationToken)
    {
        var found = await _dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == query.AccountId)
            .Join(_dbContext.Plans.AsNoTracking(), a => a.PlanId, p => p.Id, (a, p) => new { Account = a, Plan = p })
            .SingleOrDefaultAsync(cancellationToken);

        if (found is null)
        {
            return Result<AccountDetailDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        var resellerName = await _resellerNames.NameOfAsync(found.Account.ResellerId, cancellationToken);
        return Result<AccountDetailDto>.Ok(
            AccountDetailMapper.ToDto(found.Account, found.Plan, _displayNames, resellerName));
    }
}
