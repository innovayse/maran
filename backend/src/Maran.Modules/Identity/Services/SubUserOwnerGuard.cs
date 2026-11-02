using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// The one question every sub-user operation asks first: is the caller the OWNER of the account their
/// token currently speaks for, and which account is that.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a service and not a line in each handler.</b> Four operations (list, invite, change grants,
/// remove) need exactly this refusal, and it is the security-relevant part of all four: a copy that
/// forgets the role check would let a team member add their own teammates. It is asked once, here.
/// </para>
/// <para>
/// <b>The account comes from the caller's token, never from a request.</b> There is no account id in
/// any sub-user route or body, so there is no way to name somebody else's account; the rows the
/// operations then read are filtered by this id, and a sub-user of another account is simply not found
/// (404, rules/security.md item 6).
/// </para>
/// <para>
/// <b>Why 403 for a member and not 404.</b> The refusal says nothing about any other account: it
/// answers "you, in your own account, may not do this". <c>Forbidden</c> is the kind rules/csharp.md
/// reserves for exactly that.
/// </para>
/// </remarks>
public sealed class SubUserOwnerGuard
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The authenticated caller and the account their token speaks for.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the guard.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="currentUser">The authenticated caller.</param>
    public SubUserOwnerGuard(IdentityDbContext dbContext, ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    /// <summary>Finds the account the caller owns and is currently acting for.</summary>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The account id, or <c>SubUsersOwnerOnly</c> (<see cref="ErrorType.Forbidden"/>) for a caller with
    /// no account (an administrator), and for a team member of the account.
    /// </returns>
    public async Task<Result<Guid>> RequireOwnedAccountAsync(CancellationToken cancellationToken)
    {
        if (_currentUser.AccountId is not { } accountId)
        {
            return Result<Guid>.Fail(Error.Of(nameof(ErrorMessages.SubUsersOwnerOnly), ErrorType.Forbidden));
        }

        var userId = _currentUser.UserId;
        var owns = await _dbContext.AccountMemberships
            .AsNoTracking()
            .AnyAsync(
                membership => membership.UserId == userId
                    && membership.AccountId == accountId
                    && membership.Role == MembershipRole.Owner,
                cancellationToken);

        return owns
            ? Result<Guid>.Ok(accountId)
            : Result<Guid>.Fail(Error.Of(nameof(ErrorMessages.SubUsersOwnerOnly), ErrorType.Forbidden));
    }
}
