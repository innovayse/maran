using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Interfaces;
using Maran.Modules.Databases.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Databases.Tests.TestSupport;

/// <summary>
/// The user limit gate without PostgreSQL's advisory lock, for handler tests on the in-memory provider.
/// It counts and inserts exactly as the real gate does, minus the lock and the transaction, which the
/// in-memory provider has neither of.
/// </summary>
public sealed class LocklessDatabaseUserLimitGate : IDatabaseUserLimitGate
{
    /// <summary>The context the row is written through.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>Creates the gate over a test context.</summary>
    /// <param name="dbContext">The context the row is written through.</param>
    public LocklessDatabaseUserLimitGate(DatabasesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>When set, the next claim is refused as if a concurrent creation took the last slot.</summary>
    public bool RefuseNextClaim { get; set; }

    /// <inheritdoc/>
    public async Task<bool> TryTakeAsync(DatabaseUser user, int allowance, CancellationToken cancellationToken)
    {
        if (RefuseNextClaim)
        {
            RefuseNextClaim = false;

            return false;
        }

        var taken = await _dbContext.DatabaseUsers
            .CountAsync(
                existing => existing.AccountId == user.AccountId
                    && !_dbContext.Databases.Any(database =>
                        database.AccountId == existing.AccountId && database.DbUserNameSuffix == existing.NameSuffix),
                cancellationToken);
        if (taken >= allowance)
        {
            return false;
        }

        _dbContext.DatabaseUsers.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
