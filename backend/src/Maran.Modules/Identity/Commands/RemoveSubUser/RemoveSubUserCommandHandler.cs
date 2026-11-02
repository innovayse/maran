using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Extensions;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.RemoveSubUser;

/// <summary>
/// Handles <see cref="RemoveSubUserCommand"/>: ends a sub-user's access to the caller's account and
/// closes the sessions that were speaking for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Access ends at once, not when the sub-user's token expires.</b> The membership row is removed in
/// the same save that revokes every live session of that person for this account, so the refresh token
/// stops renewing; and every gated endpoint reads the membership on each call
/// (<c>IAccountMemberGrants</c>, which fails closed on a missing row), so the access token already in
/// their hands is refused there immediately. <b>Stated limit:</b> an endpoint that is not gated by a
/// feature (sites, for instance) is not asking that question, and accepts the old access token until
/// it expires — at most its fifteen-minute lifetime.
/// </para>
/// <para>
/// <b>The login goes with the access when it was made for this team only.</b> A login that was created
/// by an invitation to this account and holds no other membership would otherwise stay behind as a working
/// sign-in to nothing, holding a name and an address nobody could reuse. It is deleted — its sessions,
/// tokens and recovery codes with it. A login that still has another membership
/// stays; only this account's access and sessions are withdrawn.
/// </para>
/// <para>
/// <b>Only the caller's own team is reachable</b>, with the same 404 for another account's sub-user, the
/// caller themselves and an unknown id that <c>ChangeSubUserGrantsCommandHandler</c> gives.
/// </para>
/// </remarks>
public sealed class RemoveSubUserCommandHandler
{
    /// <summary>The module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Establishes that the caller owns the account they are acting for.</summary>
    private readonly SubUserOwnerGuard _guard;

    /// <summary>Records the removal.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The authenticated owner — the actor the journal entry names.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="guard">Establishes that the caller owns the account they are acting for.</param>
    /// <param name="journal">Records the removal.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated owner.</param>
    public RemoveSubUserCommandHandler(
        IdentityDbContext dbContext,
        SubUserOwnerGuard guard,
        IdentityAuditJournal journal,
        IClock clock,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _guard = guard;
        _journal = journal;
        _clock = clock;
        _currentUser = currentUser;
    }

    /// <summary>Removes the sub-user.</summary>
    /// <param name="command">Whom to remove.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Success once the access has ended, or a typed refusal.</returns>
    public async Task<Result> HandleAsync(RemoveSubUserCommand command, CancellationToken cancellationToken)
    {
        var owned = await _guard.RequireOwnedAccountAsync(cancellationToken);
        if (!owned.IsSuccess)
        {
            return await RefuseAsync(command, owned.Error!, cancellationToken);
        }

        var accountId = owned.Value;
        var membership = await _dbContext.AccountMemberships.SingleOrDefaultAsync(
            candidate => candidate.UserId == command.UserId
                && candidate.AccountId == accountId
                && candidate.Role == MembershipRole.Member,
            cancellationToken);
        var user = membership is null
            ? null
            : await _dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);

        if (membership is null || user is null)
        {
            return await RefuseAsync(
                command, Error.Of(nameof(ErrorMessages.SubUserNotFound), ErrorType.NotFound), cancellationToken);
        }

        // Memberships are the whole truth about what a login may reach (AccountMembership's remarks),
        // so "has other access" is a question about them and nothing else.
        var hasOtherAccess = await _dbContext.AccountMemberships.AnyAsync(
            other => other.UserId == user.Id && other.AccountId != accountId,
            cancellationToken);

        _dbContext.AccountMemberships.Remove(membership);

        if (hasOtherAccess)
        {
            // The login lives on; only the sessions speaking for THIS account end.
            var now = _clock.UtcNow;
            var speaking = await _dbContext.Sessions
                .Where(session => session.UserId == user.Id && session.ActiveAccountId == accountId)
                .ToListAsync(cancellationToken);
            foreach (var session in speaking)
            {
                session.Revoke(now, SessionRevocationReason.MembershipRemoved);
            }
        }
        else
        {
            // Sessions, invitation and reset tokens and recovery codes cascade with the login.
            _dbContext.Users.Remove(user);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordIdentifiedAsync(
            _currentUser.RequirePrincipal().UserId,
            AuditActions.SubUserRemoved,
            user.Username,
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result.Ok();
    }

    /// <summary>Journals a refused attempt and answers with the refusal.</summary>
    /// <param name="command">The refused request.</param>
    /// <param name="error">Why it was refused.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> RefuseAsync(
        RemoveSubUserCommand command,
        Error error,
        CancellationToken cancellationToken)
    {
        // The subject is the id the caller named: a refused attempt on somebody else's sub-user still
        // leaves a trace of what was probed.
        await _journal.RecordIdentifiedAsync(
            _currentUser.RequirePrincipal().UserId,
            AuditActions.SubUserRemoved,
            command.UserId.ToString("D"),
            command.IpAddress,
            command.UserAgent,
            succeeded: false,
            cancellationToken);

        return Result.Fail(error);
    }
}
