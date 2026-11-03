using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Decides which hosting account a sign-in speaks for: the one question every token the module
/// issues asks, so that a login holding several accounts gets the same answer at sign-in and at
/// every refresh.
/// </summary>
/// <remarks>
/// <para>
/// <b>The answer always comes from the login's memberships, and only from the active ones.</b> A
/// remembered choice (<see cref="Session.ActiveAccountId"/>) is honoured only while the login still
/// holds an <see cref="MembershipState.Active"/> membership of that account; otherwise it is
/// replaced, so a membership that was removed, an account that was deleted, or an account that was
/// suspended stops being acted for at the next refresh instead of lingering in a session row.
/// </para>
/// <para>
/// <b>The default is the oldest owned account</b>, then the oldest account of any other kind, so the
/// choice is deterministic and a login that owns an account never starts out inside somebody else's.
/// An administrator owns none and always gets <c>null</c>: they reach every account without acting
/// for one.
/// </para>
/// <para>
/// <b>A customer whose every account is suspended gets a refusal, not a <c>null</c>.</b> Falling
/// back to "no account" would sign them in to a panel with nothing behind it; the refusal is the
/// same <c>AccountMembershipSuspended</c> for sign-in, refresh and switch, and each caller decides
/// how much of it a stranger may be told (sign-in and refresh answer with the generic refusal).
/// A customer with no membership at all is a different, older case and still gets <c>null</c>.
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
    /// <paramref name="current"/> while the login still holds an active membership of it; otherwise
    /// the default described in the type's remarks; null for an administrator and for a customer with
    /// no membership; or <c>AccountMembershipSuspended</c> when the login holds memberships and every
    /// one of them is suspended.
    /// </returns>
    public async Task<Result<Guid?>> ResolveAsync(User user, Guid? current, CancellationToken cancellationToken)
    {
        if (user.Role != UserRole.Customer)
        {
            return Result<Guid?>.Ok(null);
        }

        var usable = _dbContext.AccountMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == user.Id && membership.State == MembershipState.Active);

        if (current is { } kept
            && await usable.AnyAsync(membership => membership.AccountId == kept, cancellationToken))
        {
            return Result<Guid?>.Ok(kept);
        }

        // Owned accounts sort before the others by a computed rank rather than by the Role column:
        // the role is stored as its name, and "Member" sorts before "Owner" alphabetically.
        var chosen = await usable
            .OrderBy(membership => membership.Role == MembershipRole.Owner ? 0 : 1)
            .ThenBy(membership => membership.CreatedAt)
            .ThenBy(membership => membership.AccountId)
            .Select(membership => (Guid?)membership.AccountId)
            .FirstOrDefaultAsync(cancellationToken);

        if (chosen is null && await IsSuspendedEverywhereAsync(user, cancellationToken))
        {
            return Result<Guid?>.Fail(Error.Of(nameof(ErrorMessages.AccountMembershipSuspended), ErrorType.Conflict));
        }

        return Result<Guid?>.Ok(chosen);
    }

    /// <summary>
    /// Whether the login holds at least one membership and every one of them is suspended: the case
    /// in which it has nothing left to act for. Asked before the second factor too, so a person in
    /// this position is refused at the password step rather than after a code they typed for nothing.
    /// </summary>
    /// <param name="user">The login signing in.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns><c>true</c> for a customer whose memberships are all suspended; <c>false</c> otherwise.</returns>
    public async Task<bool> IsSuspendedEverywhereAsync(User user, CancellationToken cancellationToken)
    {
        if (user.Role != UserRole.Customer)
        {
            return false;
        }

        var memberships = _dbContext.AccountMemberships.AsNoTracking().Where(membership => membership.UserId == user.Id);

        return await memberships.AnyAsync(cancellationToken)
            && !await memberships.AnyAsync(membership => membership.State == MembershipState.Active, cancellationToken);
    }
}
