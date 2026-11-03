using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Serialises the invitations of one account by locking the account's owner membership row for the
/// length of a transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the owner membership row.</b> Every account has exactly one (<c>UX_AccountMemberships_AccountId_Owner</c>),
/// the invitation handler has already established that it exists by establishing that the caller owns
/// the account, and a row lock needs no key derivation: the lock IS the account's row, so there is no
/// hash of an id to get wrong and no namespace to collide in. The lock is released by the commit or the
/// rollback, so it cannot outlive the request even if the process is killed.
/// </para>
/// <para>
/// <b>What it holds back, and what it does not.</b> A second invitation for the same account waits at
/// the <c>FOR UPDATE</c> until the first transaction ends, then counts the first one's committed row.
/// Other writers of that membership (suspending the account, withdrawing the owner) wait too, for the
/// few milliseconds an invitation takes. It serialises only callers that TAKE it, so it is a
/// convention rather than a constraint: a future path that adds a member without coming through here
/// is not held back, and the database will not object.
/// </para>
/// </remarks>
public sealed class SubUserTeamLock : ISubUserTeamLock
{
    /// <summary>The module's database context, and the connection the lock is taken on.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the lock.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public SubUserTeamLock(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task<IDbContextTransaction> BeginAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // raw-sql: a row lock (FOR UPDATE) has no LINQ translation and no EF Core equivalent.
            // Parameterized through ExecuteSqlInterpolatedAsync (rules/csharp.md "Data & tenancy"):
            // the account id and the role name are parameters, and the only literals are the
            // module's own schema and table, quoted because the names are PascalCase.
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                select 1 from identity."AccountMemberships"
                where "AccountId" = {accountId} and "Role" = {nameof(MembershipRole.Owner)}
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
