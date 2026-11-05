using Maran.Modules.Sftp.Interfaces;
using Maran.Modules.Sftp.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Sftp.Services;

/// <summary>
/// Serialises the key changes of one SFTP login with a <c>FOR UPDATE</c> lock on the login's own row.
/// </summary>
/// <remarks>
/// <para>
/// The lock IS the login's row, so there is no key to derive and no namespace to collide in, and it
/// is released by the commit or the rollback of the transaction it was taken in, so it cannot
/// outlive the request even if the process is killed. A second change on the same login waits at the
/// <c>FOR UPDATE</c> until the first transaction ends.
/// </para>
/// <para>
/// It serialises only callers that TAKE it, so it is a convention rather than a constraint: a future
/// path that changes a login's keys without coming through here is not held back, and the database
/// will not object. The lock is held across the agent call, by design: the agent's own deadlines
/// bound it, and releasing it before the agent answered would let the next request read a set the
/// agent has not yet been given.
/// </para>
/// <para>
/// The row is locked by id with no tenant predicate, so callers load the login through the
/// tenant-filtered context FIRST and lock only a row they have already been allowed to see.
/// </para>
/// </remarks>
public sealed class SftpUserKeyLock : ISftpUserKeyLock
{
    /// <summary>The module's database context, and the connection the lock is taken on.</summary>
    private readonly SftpDbContext _dbContext;

    /// <summary>Creates the lock.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public SftpUserKeyLock(SftpDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IDbContextTransaction> BeginAsync(Guid sftpUserId, CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // raw-sql: a row lock (FOR UPDATE) has no LINQ translation and no EF Core equivalent.
            // Parameterized through ExecuteSqlInterpolatedAsync (rules/csharp.md "Data & tenancy"):
            // the login id is a parameter, and the only literals are the module's own schema and
            // table, quoted because the names are PascalCase.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                select 1 from sftp."SftpUsers"
                where "Id" = {sftpUserId}
                for update
                """,
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
