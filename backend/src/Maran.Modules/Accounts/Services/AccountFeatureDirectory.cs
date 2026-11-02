using System.Diagnostics;
using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Callers;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// The Accounts module's implementation of <see cref="IAccountFeatureDirectory"/>: the one place
/// another module may learn whether an account's plan includes a feature, without referencing this
/// module or reading its schema.
/// </summary>
/// <remarks>
/// <para>
/// The tenant scope is applied HERE, by the same comparison <see cref="AccountDirectory.FindAsync"/>
/// makes against the caller's own token: an administrator may ask about any account, a customer
/// only about the one they own. For any other account the answer is <c>false</c>, indistinguishable
/// from a plan that lacks the feature, so a guessed account id learns nothing about another tenant.
/// </para>
/// <para>
/// It reads the plan on every call and caches nothing, so a plan edit reaches the customer's next
/// request. The cost is one indexed join per gated request, which is what an immediate refusal is
/// worth; a cache would reintroduce a window in which a removed feature still works.
/// </para>
/// </remarks>
public sealed class AccountFeatureDirectory : IAccountFeatureDirectory
{
    /// <summary>The Accounts module's database context; this module owns the accounts schema.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The authenticated principal, whose tenant scope bounds every answer.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the directory.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="currentUser">The authenticated principal of the current request.</param>
    public AccountFeatureDirectory(AccountsDbContext dbContext, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    /// <inheritdoc />
    public async Task<bool> IncludesAsync(Guid accountId, AccountFeature feature, CancellationToken cancellationToken)
    {
        // The comparison is against the id the caller's own token carries, never against anything on
        // the request, so a forged or guessed account id cannot widen it.
        if (!Sees(accountId))
        {
            return false;
        }

        // Expression lambdas: EF Core translates the join into SQL. The plan is read whole and
        // asked afterwards, so the rule "includes" is stated once, on the entity.
        var sold = await _dbContext.Accounts
            .AsNoTracking()
            .Where(account => account.Id == accountId)
            .Join(
                _dbContext.Plans.AsNoTracking(),
                account => account.PlanId,
                plan => plan.Id,
                (account, plan) => plan)
            .SingleOrDefaultAsync(cancellationToken);

        return sold is not null && sold.Includes(feature);
    }

    /// <summary>
    /// Whether the caller may see the account <paramref name="accountId"/>: an administrator any
    /// account, a customer only the one their own token names, and nobody else (an anonymous caller,
    /// a customer login acting for no account) none.
    /// </summary>
    /// <param name="accountId">The account asked about.</param>
    /// <returns>True when the caller's tenant scope includes the account.</returns>
    private bool Sees(Guid accountId)
    {
        return _currentUser.Caller switch
        {
            AdministratorCaller => true,
            CustomerCaller customer => customer.AccountId == accountId,
            AnonymousCaller => false,
            AccountlessCustomerCaller => false,
            _ => throw new UnreachableException("Caller is sealed to SharedKernel: four cases exist."),
        };
    }
}
