using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Queries.GetMyAccount;

/// <summary>
/// Handles <see cref="GetMyAccountQuery"/>: reads the account named by the CALLER'S OWN token and
/// its plan's limits, with the plan's name resolved for the request culture.
/// </summary>
/// <remarks>
/// The account id never comes from the request — there is no route or query parameter naming one —
/// so there is nothing here for a caller to forge. An administrator carries no
/// <see cref="ICurrentUser.AccountId"/> and gets the same <c>AccountNotFound</c> as anybody else
/// without one: this endpoint answers "the account you own", and an administrator owns none. That
/// is not a gap; the administrator already has the whole list through
/// <see cref="Controllers.AccountsController"/>.
/// </remarks>
public sealed class GetMyAccountQueryHandler
{
    /// <summary>The authenticated principal of the current request.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Resolves the account's plan's display-name key in the current request culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler with the caller identity, the module's database context, and its resource localizer.</summary>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="displayNames">Resolves the account's plan's display-name key in the current request culture.</param>
    public GetMyAccountQueryHandler(
        ICurrentUser currentUser, AccountsDbContext dbContext, IStringLocalizer<DisplayNames> displayNames)
    {
        _currentUser = currentUser;
        _dbContext = dbContext;
        _displayNames = displayNames;
    }

    /// <summary>Returns the caller's own account and plan limits, or a typed failure when they own none.</summary>
    /// <param name="query">The (parameterless) request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The caller's account as a <see cref="MyAccountDto"/>, or <c>AccountNotFound</c>.</returns>
    public async Task<Result<MyAccountDto>> HandleAsync(GetMyAccountQuery query, CancellationToken cancellationToken)
    {
        // Read off the token, never off the request. There is no parameter to forge because there
        // is no parameter.
        if (_currentUser.AccountId is not { } accountId)
        {
            return Result<MyAccountDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        // Expression lambda in the join projection, as AccountDirectory uses: EF Core translates
        // this into SQL, and a statement-bodied lambda cannot become an expression tree.
        var row = await _dbContext.Accounts
            .AsNoTracking()
            .Where(account => account.Id == accountId)
            .Join(
                _dbContext.Plans.AsNoTracking(),
                account => account.PlanId,
                plan => plan.Id,
                (account, plan) => new { account, plan })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return Result<MyAccountDto>.Fail(Error.Of(nameof(ErrorMessages.AccountNotFound), ErrorType.NotFound));
        }

        var planDto = new MyAccountPlanDto(
            _displayNames[row.plan.DisplayNameKey],
            row.plan.DiskQuotaMb,
            row.plan.MaxSites,
            row.plan.MaxDatabases,
            row.plan.MaxSftpUsers,
            row.plan.MaxFtpUsers,
            row.plan.MaxCronEntries);

        return Result<MyAccountDto>.Ok(
            new MyAccountDto(row.account.Id, row.account.Name, row.account.PrimaryDomain, row.account.Status, planDto));
    }
}
