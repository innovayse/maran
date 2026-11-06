using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Serialises API key creation across the whole panel with a transaction-scoped PostgreSQL advisory
/// lock.
/// </summary>
/// <remarks>
/// <para>
/// The lock is scoped to the TRANSACTION on the module's connection, so the commit or the rollback
/// releases it and it cannot outlive the request even if the process is killed; a session-scoped lock
/// would need a release that a crash could skip. A second creation waits at the lock until the first
/// transaction ends.
/// </para>
/// <para>
/// It serialises only callers that TAKE it, so it is a convention rather than a constraint: a future
/// path that inserts keys without coming through here is not held back, and the database will not
/// object. The unique index on the prefix remains the last line of defence for a collision.
/// </para>
/// </remarks>
public sealed class ApiKeyCreationLock : IApiKeyCreationLock
{
    /// <summary>
    /// The first key of the two-key advisory lock: it spells <c>APIK</c> in ASCII, so an operator
    /// reading <c>pg_locks</c> can tell whose lock a blocked session waits for.
    /// </summary>
    private const int AdvisoryLockNamespace = 0x4150494B;

    /// <summary>The second key: the panel holds one key-creation lock, so there is nothing to derive.</summary>
    private const int AdvisoryLockKey = 0;

    /// <summary>The module's database context, and the connection the lock is taken on.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the lock.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public ApiKeyCreationLock(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IDbContextTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // raw-sql: pg_advisory_xact_lock has no LINQ translation and no EF Core equivalent — a
            // lock scoped to a transaction is a PostgreSQL facility. Parameterized through
            // ExecuteSqlInterpolatedAsync (rules/csharp.md "Data & tenancy"): both keys are
            // constants of this file, passed as parameters so nothing here is interpolated SQL.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"select pg_advisory_xact_lock({AdvisoryLockNamespace}, {AdvisoryLockKey})",
                cancellationToken);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }

        return transaction;
    }
}
