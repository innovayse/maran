using Maran.Modules.Accounts.Persistence;

namespace Maran.Modules.Accounts.Queries.CountAccounts;

/// <summary>Handles <see cref="CountAccountsQuery"/> with one <c>count(*)</c> over <c>accounts.Accounts</c>.</summary>
public sealed class CountAccountsQueryHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the handler with the module's own database context.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public CountAccountsQueryHandler(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Counts every account; the caller gates itself to administrators, as the dashboard does.</summary>
    /// <param name="query">The (parameterless) count request.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A successful result carrying the count; this operation never fails.</returns>
    public async Task<Result<int>> HandleAsync(CountAccountsQuery query, CancellationToken cancellationToken)
    {
        return Result<int>.Ok(await _dbContext.Accounts.CountAsync(cancellationToken));
    }
}
