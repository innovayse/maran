using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Finds the login that owns a hosting account: the one question the administrator-facing paths
/// (invitation, e-mail change, view-as, account creation) ask about an account, answered from the
/// owner membership and nowhere else.
/// </summary>
/// <remarks>
/// A login row names no account, so "the owner of account X" is the owner
/// <see cref="Domain.Entities.AccountMembership"/> of X. The database guarantees there is at most
/// one (<c>UX_AccountMemberships_AccountId_Owner</c>), which is what lets this answer with a single
/// row and not a choice.
/// </remarks>
public sealed class AccountOwnerFinder
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the finder.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public AccountOwnerFinder(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Finds the owner of an account, tracked, so the caller can change and save it.</summary>
    /// <param name="accountId">The hosting account.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The owning login and its membership, or null when the account has no owner login.</returns>
    public async Task<AccountOwner?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var found = await (
            from membership in _dbContext.AccountMemberships
            where membership.AccountId == accountId && membership.Role == MembershipRole.Owner
            join login in _dbContext.Users on membership.UserId equals login.Id
            select new { login, membership }).SingleOrDefaultAsync(cancellationToken);

        return found is null ? null : new AccountOwner(found.login, found.membership);
    }

    /// <summary>Whether the account already has an owner login.</summary>
    /// <param name="accountId">The hosting account.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns><c>true</c> when an owner membership of the account exists.</returns>
    public async Task<bool> ExistsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        return await _dbContext.AccountMemberships
            .AnyAsync(
                membership => membership.AccountId == accountId && membership.Role == MembershipRole.Owner,
                cancellationToken);
    }
}
