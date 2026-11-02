using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Decides which hosting account a sign-in speaks for: the one question every token the module
/// issues asks, so that a login holding several accounts gets the same answer at sign-in and at
/// every refresh.
/// </summary>
/// <remarks>
/// <para>
/// <b>The answer always comes from the login's memberships.</b> A remembered choice
/// (<see cref="Session.ActiveAccountId"/>) is honoured only while the login still holds a
/// membership of that account; otherwise it is replaced, so a membership that was removed — or an
/// account that was deleted, which removes its memberships — stops being acted for at the next
/// refresh instead of lingering in a session row.
/// </para>
/// <para>
/// <b>The default is the oldest owned account</b>, then the oldest account of any other kind, so the
/// choice is deterministic and a login that owns an account never starts out inside somebody else's.
/// An administrator owns none and always gets <c>null</c>: they reach every account without acting
/// for one.
/// </para>
/// </remarks>
public sealed class ActiveAccountResolver
{
    /// <summary>The module's database context, read for the memberships.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the resolver.</summary>
    /// <param name="dbContext">The module's database context.</param>
    public ActiveAccountResolver(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Resolves the account a sign-in of <paramref name="user"/> speaks for.</summary>
    /// <param name="user">The login signing in or refreshing.</param>
    /// <param name="current">
    /// The account the session already speaks for, or null at sign-in when there is no session yet.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// <paramref name="current"/> while the login still holds a membership of it; otherwise the
    /// default described in the type's remarks; null for an administrator and for a customer with
    /// no membership.
    /// </returns>
    public async Task<Guid?> ResolveAsync(User user, Guid? current, CancellationToken cancellationToken)
    {
        if (user.Role != UserRole.Customer)
        {
            return null;
        }

        var memberships = _dbContext.AccountMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == user.Id);

        if (current is { } kept
            && await memberships.AnyAsync(membership => membership.AccountId == kept, cancellationToken))
        {
            return kept;
        }

        // Owned accounts sort before the others by a computed rank rather than by the Role column:
        // the role is stored as its name, and "Member" sorts before "Owner" alphabetically.
        return await memberships
            .OrderBy(membership => membership.Role == MembershipRole.Owner ? 0 : 1)
            .ThenBy(membership => membership.CreatedAt)
            .ThenBy(membership => membership.AccountId)
            .Select(membership => (Guid?)membership.AccountId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
