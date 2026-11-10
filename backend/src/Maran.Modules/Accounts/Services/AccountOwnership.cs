using Maran.Modules.Accounts.Persistence;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Answers <see cref="IAccountOwnership"/> from the accounts table, with no caller scope.
/// </summary>
/// <remarks>
/// The query filters are bypassed on purpose: the asker is the bearer handler, which runs before the
/// request has a principal, and the fail-closed filters would answer an anonymous caller "not owned" for
/// every account. The bypass is safe because the method takes both ids from the asker's own session row
/// and returns one boolean, so it can confirm nothing the asker did not already hold.
/// </remarks>
public sealed class AccountOwnership : IAccountOwnership
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the window.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public AccountOwnership(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<bool> IsOwnedByResellerAsync(Guid accountId, Guid resellerId, CancellationToken cancellationToken)
    {
#pragma warning disable RS0030 // the bearer handler has no principal yet; the fail-closed filter would answer nothing
        var accounts = _dbContext.Accounts.IgnoreQueryFilters().AsNoTracking();
#pragma warning restore RS0030

        return await accounts.AnyAsync(a => a.Id == accountId && a.ResellerId == resellerId, cancellationToken);
    }
}
