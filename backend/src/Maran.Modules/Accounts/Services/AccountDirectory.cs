using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// The Accounts module's implementation of <see cref="IAccountDirectory"/>: the one place another
/// module may learn an account's system user name and its plan's allowances, without ever
/// referencing this module (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// The tenant scope is applied HERE, in the query, rather than left to the calling module. A
/// cross-module abstraction is the natural gap in a tenancy story: the caller's own
/// <c>DbContext</c> query filter cannot reach across into this schema, so if this type answered
/// every id it was handed, a customer could learn another tenant's system user name — the name that
/// addresses every agent operation — simply by guessing an account id.
/// </remarks>
public sealed class AccountDirectory : IAccountDirectory
{
    /// <summary>The Accounts module's database context; this module owns the accounts schema.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The authenticated principal, whose tenant scope bounds every answer.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the directory.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public AccountDirectory(AccountsDbContext dbContext, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        // An administrator sees every account; a customer sees exactly the one they own. The
        // comparison is against the id the caller's own token carries, never against anything on
        // the request, so a forged or guessed account id cannot widen it.
        if (!_currentUser.IsAdmin && _currentUser.AccountId != accountId)
        {
            return null;
        }

        // Expression lambdas, not the braced form used elsewhere in this repository: EF Core
        // translates these into SQL, and a statement-bodied lambda cannot become an expression tree.
        return await _dbContext.Accounts
            .AsNoTracking()
            .Where(account => account.Id == accountId)
            .Join(
                _dbContext.Plans.AsNoTracking(),
                account => account.PlanId,
                plan => plan.Id,
                (account, plan) => new AccountSnapshot(
                    account.Id,
                    account.Name,
                    plan.MaxSites,
                    plan.MaxDatabases,
                    plan.MaxSftpUsers,
                    plan.MaxPhpWorkersPerPool))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
