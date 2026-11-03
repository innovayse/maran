using Maran.Modules.Accounts.Persistence;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Answers, server-wide, whether an account name or a primary domain is already claimed — the one
/// read the creation of an account asks before it provisions anything.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately ignores the query filters: an account name is the Linux user name and the login
/// name, and a domain is claimed once, across every tenant. A reseller's context hides every account
/// another owner holds, so with the filters on the pre-check would pass a name an administrator's
/// account or another reseller's account already uses, and the creation would run on to the agent and
/// the unique index. The same holds for the name of a reseller, which is also the name of its login:
/// one namespace with the account names.
/// </para>
/// <para>
/// The answer is a yes or a no and never a row, so a reseller learns that a name exists (the
/// accepted residual of a server-global namespace, rules/security.md item 6 and the reseller threat
/// note) and nothing about who holds it.
/// </para>
/// </remarks>
public sealed class AccountNamespaceClaims
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the reader.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public AccountNamespaceClaims(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Whether any account or any reseller already uses <paramref name="name"/>.</summary>
    /// <param name="name">The name being claimed, exactly as it would be stored.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><c>true</c> when the name is some account's or some reseller's.</returns>
    public async Task<bool> IsNameTakenAsync(string name, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // an account name is one namespace across every tenant; a scoped read would let a reseller reuse a name it cannot see
        var accountTakes = await _dbContext.Accounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(account => account.Name == name, cancellationToken);

        return accountTakes || await _dbContext.Resellers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(reseller => reseller.Name == name, cancellationToken);
#pragma warning restore RS0030
    }

    /// <summary>Whether any account already uses <paramref name="domain"/> as its primary domain.</summary>
    /// <param name="domain">The domain being claimed, exactly as it would be stored.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><c>true</c> when the domain is some account's primary domain.</returns>
    public async Task<bool> IsDomainTakenAsync(string domain, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // a primary domain is claimed once across every tenant; a scoped read would let a reseller claim a domain it cannot see
        return await _dbContext.Accounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(account => account.PrimaryDomain == domain, cancellationToken);
#pragma warning restore RS0030
    }
}
