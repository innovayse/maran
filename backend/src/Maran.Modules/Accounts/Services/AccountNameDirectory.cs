using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// The Accounts module's implementation of <see cref="IAccountNameDirectory"/>: account names for the
/// header's account selector, read without the caller's tenant scope.
/// </summary>
/// <remarks>
/// No scope is applied here, deliberately and by contract — see the interface, which puts the
/// authorization burden on the caller. There is no <c>IgnoreQueryFilters()</c> call either:
/// <see cref="AccountsDbContext"/>'s query filter narrows a reseller caller and no other, and this
/// window answers the header's account selector, which a reseller does not have, so the call would
/// suppress nothing any caller of this window sees (the same reasoning
/// <see cref="AccountDirectory.ListAsync"/> records). A reseller that did reach it would be shown the
/// names of its own accounts only, which is the safe direction.
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
        return await _dbContext.Accounts
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
    }
}
