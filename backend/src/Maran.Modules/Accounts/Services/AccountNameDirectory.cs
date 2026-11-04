using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// The Accounts module's implementation of <see cref="IAccountNameDirectory"/>: account names for the
/// header's account selector, read without the caller's tenant scope.
/// </summary>
/// <remarks>
/// No caller scope is applied here, deliberately and by contract, see the interface, which puts the
/// authorization burden on the caller. <see cref="AccountsDbContext"/> fails closed: an anonymous or
/// accountless caller sees no account at all, and a customer sees only the account it is acting for.
/// This window serves the header's account selector, which lists whichever accounts the login holds,
/// the active one included and every one of them for a customer acting for none, so the read bypasses
/// the query filter explicitly with <c>IgnoreQueryFilters()</c> and an inline suppression carrying the
/// reason. The ids come from the caller's own memberships, which the caller has already resolved, and
/// only id and name are returned.
/// </remarks>
public sealed class AccountNameDirectory : IAccountNameDirectory
{
    /// <summary>The Accounts module's database context; this module owns the accounts schema.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the directory.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public AccountNameDirectory(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken)
    {
        var requested = accountIds.Distinct().ToList();
        if (requested.Count == 0)
        {
            return new Dictionary<Guid, string>();
        }

        // The selectors are delegates run after the read, not translated, so they are braced.
#pragma warning disable RS0030 // ids come from the caller's own memberships; only id and name are returned
        return await _dbContext.Accounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(account => requested.Contains(account.Id))
            .ToDictionaryAsync(
                account =>
                {
                    return account.Id;
                },
                account =>
                {
                    return account.Name;
                },
                cancellationToken);
#pragma warning restore RS0030
    }
}
