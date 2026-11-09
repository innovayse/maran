using System.Buffers.Binary;
using Maran.Modules.Databases.Domain.Entities;
using Maran.Modules.Databases.Interfaces;
using Maran.Modules.Databases.Persistence;

namespace Maran.Modules.Databases.Services;

/// <summary>
/// Takes the account's last free additional-user slot atomically, with a PostgreSQL advisory lock keyed
/// by the account so the count and the insert behind it cannot be interleaved by another request.
/// </summary>
/// <remarks>
/// The argument for a lock rather than an index, an insert carrying the count, a serializable
/// transaction or a counter column is <see cref="DatabaseSlotGate"/>'s, and holds here unchanged. Only
/// the lock namespace differs, so a user creation never waits on a database creation of the same account.
/// </remarks>
public sealed class DatabaseUserLimitGate : IDatabaseUserLimitGate
{
    /// <summary>The first advisory-lock key, distinct from the database slot gate's.</summary>
    private const int AdvisoryLockNamespace = 0x4D595553;

    /// <summary>The module's database context, whose connection carries the transaction and the lock.</summary>
    private readonly DatabasesDbContext _dbContext;

    /// <summary>Creates the gate over the request's own context.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public DatabaseUserLimitGate(DatabasesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc/>
    public async Task<bool> TryTakeAsync(DatabaseUser user, int allowance, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // raw-sql: pg_advisory_xact_lock has no LINQ translation and no EF Core equivalent. Both keys are
        // integers computed here, passed as parameters through ExecuteSqlInterpolatedAsync.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"select pg_advisory_xact_lock({AdvisoryLockNamespace}, {LockKeyFor(user.AccountId)})",
            cancellationToken);

        // Counted after the lock and in its own statement, so a request that waited sees the winner's
        // committed row. A dedicated user is not counted: it belongs to a database, and the plan bounds those.
        var taken = await _dbContext.DatabaseUsers
            .CountAsync(
                existing => existing.AccountId == user.AccountId
                    && !_dbContext.Databases.Any(database =>
                        database.AccountId == existing.AccountId && database.DbUserNameSuffix == existing.NameSuffix),
                cancellationToken);
        if (taken >= allowance)
        {
            await transaction.RollbackAsync(cancellationToken);

            return false;
        }

        _dbContext.DatabaseUsers.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <summary>Folds an account id into the second advisory-lock key.</summary>
    /// <param name="accountId">The account.</param>
    /// <returns>The first four bytes of the id, as a signed integer.</returns>
    private static int LockKeyFor(Guid accountId)
    {
        Span<byte> bytes = stackalloc byte[16];
        accountId.TryWriteBytes(bytes);

        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }
}
