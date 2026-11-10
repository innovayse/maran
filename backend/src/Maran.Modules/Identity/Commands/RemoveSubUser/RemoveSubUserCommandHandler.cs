using System.Globalization;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Models;
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
/// <b>Deleting the login pseudonymises what the journal says about the person, in the same
/// transaction.</b> The rows naming the login (as the actor, or by a subject equal to its name, address
/// or id) go to <see cref="AuditPseudonymiser"/> before anything is staged, and the removal is committed
/// together with them. The summary (<c>PersonalDataErased</c>) is written after the commit. The removal
/// entry itself is written AFTER the erasure, so it names the person by the same pseudonym and not by the
/// login name: recording the name there would put back the very identifier that was just replaced. A
/// login that stays is a person who still exists, so nothing is pseudonymised for it and its removal
/// entry keeps the login name.
/// </para>
/// <para>
/// <b>The same operation withdraws a pending offer.</b> The row an offer is listed as carries the
/// offer's id, so removing that row cancels the offer and nothing else: no login and no membership is
/// touched, because the offered person has none here yet.
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

    /// <summary>Pseudonymises the journal rows of a login this handler deletes.</summary>
    private readonly AuditPseudonymiser _pseudonymiser;

    /// <summary>The panel's clock.</summary>
    private readonly IClock _clock;

    /// <summary>The authenticated owner — the actor the journal entry names.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="guard">Establishes that the caller owns the account they are acting for.</param>
    /// <param name="journal">Records the removal.</param>
    /// <param name="pseudonymiser">Pseudonymises the journal rows of a login this handler deletes.</param>
    /// <param name="clock">The panel's clock.</param>
    /// <param name="currentUser">The authenticated owner.</param>
    public RemoveSubUserCommandHandler(
        IdentityDbContext dbContext,
        SubUserOwnerGuard guard,
        IdentityAuditJournal journal,
        AuditPseudonymiser pseudonymiser,
        IClock clock,
        ICurrentUser currentUser)
    {
        _dbContext = dbContext;
        _guard = guard;
        _journal = journal;
        _pseudonymiser = pseudonymiser;
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

        if (membership is null)
        {
            // Not a member: the id may name an open offer of this account (the row an offer is listed
            // as), which removing withdraws. Anything else is a person this account has not got.
            var cancelled = await CancelOfferAsync(command, accountId, cancellationToken);
            if (cancelled is not null)
            {
                return cancelled;
            }
        }

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

        // The subject the removal is journalled under: the login name, unless the login is deleted below,
        // in which case it is the pseudonym that replaced the name in the person's earlier rows.
        var subject = user.Username;
        AuditErasureOutcome? erasure = null;

        if (hasOtherAccess)
        {
            // The login lives on; only the sessions speaking for THIS account end.
            _dbContext.AccountMemberships.Remove(membership);
            var now = _clock.UtcNow;
            var speaking = await _dbContext.Sessions
                .Where(session => session.UserId == user.Id && session.ActiveAccountId == accountId)
                .ToListAsync(cancellationToken);
            foreach (var session in speaking)
            {
                session.Revoke(now, SessionRevocationReason.MembershipRemoved);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            erasure = await DeleteLoginAsync(membership, user, cancellationToken);
            subject = erasure.Pseudonym;
        }

        if (erasure is null)
        {
            // The login lives on: it is the subject login of this row, so its later erasure finds the
            // row by identity and not by the name it carries.
            await _journal.RecordOnUserAsync(
                SignedInCallerPolicy.Require(_currentUser).UserId,
                user.Id,
                IdentityAuditActions.SubUserRemoved,
                subject,
                command.IpAddress,
                command.UserAgent,
                succeeded: true,
                cancellationToken);
        }
        else
        {
            // The login is gone and the subject is already its pseudonym: no login is left to name.
            await _journal.RecordIdentifiedAsync(
                SignedInCallerPolicy.Require(_currentUser).UserId,
                IdentityAuditActions.SubUserRemoved,
                subject,
                command.IpAddress,
                command.UserAgent,
                succeeded: true,
                cancellationToken);
        }

        if (erasure is { Rows: > 0 })
        {
            await _journal.RecordSystemAsync(
                IdentityAuditActions.PersonalDataErased,
                string.Create(CultureInfo.InvariantCulture, $"{erasure.Pseudonym} rows={erasure.Rows}"),
                succeeded: true,
                cancellationToken);
        }

        return Result.Ok();
    }

    /// <summary>Deletes a login made for this team only, pseudonymising its journal rows in the same transaction.</summary>
    /// <param name="membership">The login's membership of the caller's account.</param>
    /// <param name="user">The login to delete.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>What the pseudonymisation did; its pseudonym is the name the login is journalled under from now on.</returns>
    private async Task<AuditErasureOutcome> DeleteLoginAsync(
        AccountMembership membership,
        User user,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Before anything is staged: the pseudonymiser saves in batches, and a save writes whatever the
        // context has staged.
        var erasure = await _pseudonymiser.PseudonymiseUserAsync(user.Id, [user.Username, user.Email], cancellationToken);

        _dbContext.AccountMemberships.Remove(membership);

        // Sessions, invitation and reset tokens and recovery codes cascade with the login.
        _dbContext.Users.Remove(user);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return erasure;
    }

    /// <summary>Withdraws the open offer the command names, when it names one of this account's.</summary>
    /// <param name="command">The request; its id is tried as an offer's.</param>
    /// <param name="accountId">The account the caller owns.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Success once the offer is withdrawn, or <c>null</c> when the id names no open offer of this account.</returns>
    private async Task<Result?> CancelOfferAsync(
        RemoveSubUserCommand command,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        // Scoped to the caller's own account in the query, so another account's offer is as absent as
        // an unknown id, and answers the same 404 the caller gets for any person that is not theirs.
        var offer = await _dbContext.AccountMembershipOffers.SingleOrDefaultAsync(
            candidate => candidate.Id == command.UserId
                && candidate.AccountId == accountId
                && candidate.ResolvedAt == null,
            cancellationToken);
        if (offer is null)
        {
            return null;
        }

        offer.Cancel(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // The subject is what the owner typed, the only name an offer row has for the person.
        await _journal.RecordIdentifiedAsync(
            SignedInCallerPolicy.Require(_currentUser).UserId,
            IdentityAuditActions.SubUserRemoved,
            offer.Identifier,
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
            SignedInCallerPolicy.Require(_currentUser).UserId,
            IdentityAuditActions.SubUserRemoved,
            command.UserId.ToString("D"),
            command.IpAddress,
            command.UserAgent,
            succeeded: false,
            cancellationToken);

        return Result.Fail(error);
    }
}
