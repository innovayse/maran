using System.Globalization;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Removes the panel logins of an account that is about to be deleted, and every credential those
/// logins carry (<see cref="AccountDeleting"/>), and pseudonymises what the audit journal says about
/// those logins and about the account.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this module is different from the other subscribers.</b> Every other subscriber releases
/// records — a certificate row, a database row. This one releases a WAY IN. A login left behind by
/// a deleted account is a username and a password that still authenticate against a panel whose
/// tenant no longer exists, and a refresh token left behind is a session that keeps working without
/// anybody typing that password at all. So "leftover row" understates it here: the leftover is
/// access.
/// </para>
/// <para>
/// <b>What it owns, established from the mapping rather than assumed.</b> A login row names no
/// account, so the only entities in this module carrying an <c>AccountId</c> are
/// <c>AccountMembership</c>, <c>AccountMembershipOffer</c> (an offer to join the account's team,
/// which grants nothing but would let its addressee onto the team) and <c>AuditEvent</c>, and they are
/// the only ones the panel's residue audit can see. The rest of
/// an account's identity state hangs off the USER — <c>Session</c>, <c>RecoveryCode</c>,
/// <c>PasswordResetToken</c> and <c>InvitationToken</c> are all keyed by <c>UserId</c> — which is
/// precisely the shape the residue audit is blind to: it counts rows naming the account, and these
/// name a user. A handler that dropped the memberships alone would therefore be pronounced clean by
/// the audit while leaving live refresh tokens, and a live invitation, in the table.
/// </para>
/// <para>
/// <b>What guarantees the dependents go, stated honestly.</b> All four tables are mapped with
/// <c>DeleteBehavior.Cascade</c> from <c>User</c>, so removing a login takes them whether this
/// handler names them or not; the four <c>RemoveRange</c> calls below also name them, so the removal
/// does not depend on which provider is underneath (this module's unit tests run on the in-memory one,
/// where a foreign key is a suggestion) and so the method answers "what do you release for an
/// account" in full. What does NOT guard the mapping any more is the architecture census:
/// <c>AccountCascadeTests.Every_dependent_of_a_tenant_row_is_removed_with_it</c> walks the
/// dependents of tenant rows, and a login stopped being one when this release stopped reading its
/// account column (issue #325). The assertion that the credentials are gone is
/// <c>AccountDeletionCascadeTests</c>, against real PostgreSQL.
/// </para>
/// <para>
/// <b>What it does to the audit journal: pseudonymises, never deletes.</b> The journal is the
/// operator's record of what happened, so no row is removed. Before the logins go, the rows that name
/// each deleted login (as the actor, as the operator viewing as somebody, or by a subject equal to its
/// name, address or id) and then the rows that carry the account are handed to
/// <see cref="AuditPseudonymiser"/>, which replaces who and where with a random pseudonym and keeps
/// what, when and which administrator. That runs in the SAME database transaction as the removals, so
/// Identity's part of a deletion either removes the logins and pseudonymises their rows or does
/// neither. <b>Stated limit:</b> that is atomic for Identity's context only. The rest of an account
/// deletion runs in other modules' contexts (a handler works with one context), so a failure after this
/// handler committed leaves the logins gone and the journal pseudonymised while the account is still
/// alive; that is exactly as atomic as the removal of the logins always was, and the deletion can be
/// retried. A login kept because it holds another account is not erased: it is a person who still
/// exists. Administrator entries of the other journals that carry no account id are not reached by the
/// account rule.
/// </para>
/// <para>
/// <b>What else it leaves alone.</b> <c>SecurityPolicy</c> is a single server-wide row keyed by a
/// constant and belongs to no account. <c>FailedLoginByIp</c> is counting state keyed by an ADDRESS, and
/// clearing it because a tenant left would hand an attacker a way to reset their own lockout.
/// </para>
/// <para>
/// <b>No query filter is bypassed, and that is a fact about this module rather than an oversight.</b>
/// <c>AccountMembership</c> and <c>AccountMembershipOffer</c> are named in <c>TenantScopeTests</c>' exemption list, and <c>User</c> and
/// the rows hanging off it belong to a person rather than to an account, so the census does not see
/// them; the Identity module exposes no endpoint that lists or fetches users at all. There is
/// nothing here to ignore and no <c>IgnoreQueryFilters</c> to justify.
/// </para>
/// <para>
/// <b>A login that holds several accounts outlives any one of them.</b> Every login with a
/// membership of the deleted account loses that membership. A login left with no membership at all
/// — the owner, and any member who had only this account — is deleted with its credentials, because a
/// customer login that reaches no account is a way in to nothing; a login that still holds another
/// account is kept whole.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the deletion with the account intact — the recoverable half. The order inside is
/// pseudonymise, then dependents before principals, so a save that fails part-way cannot leave a token
/// row pointing at a user that is gone, and the transaction is rolled back by its disposal.
/// </para>
/// <para>
/// <b>It writes one summary entry per erasure, and none for the deletion itself.</b> The deletion is
/// journalled by the Accounts module, which holds the caller's address and client. What this module adds
/// is <c>PersonalDataErased</c>, written as the panel AFTER the transaction commits, whose subject holds
/// the pseudonym and a row count and never the original id, name or address. A deletion that aborted
/// therefore leaves no summary behind. An erasure that matched no row writes nothing: there is nothing
/// to account for.
/// </para>
/// </remarks>
public sealed class AccountDeletingHandler
{
    /// <summary>The Identity module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Pseudonymises the journal rows of the deleted logins and of the account.</summary>
    private readonly AuditPseudonymiser _pseudonymiser;

    /// <summary>Writes the summary entry of each erasure.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Identity module's database context.</param>
    /// <param name="pseudonymiser">Pseudonymises the journal rows of the deleted logins and of the account.</param>
    /// <param name="journal">Writes the summary entry of each erasure.</param>
    public AccountDeletingHandler(
        IdentityDbContext dbContext,
        AuditPseudonymiser pseudonymiser,
        IdentityAuditJournal journal)
    {
        _dbContext = dbContext;
        _pseudonymiser = pseudonymiser;
        _journal = journal;
    }

    /// <summary>
    /// Removes every membership of the account being removed, deletes the logins left with no
    /// membership together with everything hanging off them, and pseudonymises their journal rows and
    /// the account's.
    /// </summary>
    /// <param name="message">The account about to be deleted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the logins are gone and the journal no longer names them.</returns>
    public async Task HandleAsync(AccountDeleting message, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Everything is READ first and nothing is staged until the journal has been pseudonymised: the
        // pseudonymiser saves in batches, and a save writes whatever the context has staged.
        var offers = await _dbContext.AccountMembershipOffers
            .Where(offer => offer.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
        var memberships = await _dbContext.AccountMemberships
            .Where(membership => membership.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
        var users = await FindLoginsToDeleteAsync(message.AccountId, memberships, cancellationToken);

        var erasures = new List<AuditErasureOutcome>();
        foreach (var user in users)
        {
            erasures.Add(await _pseudonymiser.PseudonymiseUserAsync(user.Id, [user.Username, user.Email], cancellationToken));
        }

        // The account's rows after the people's, so a row that names a deleted login is handled by the
        // person rule first.
        erasures.Add(await _pseudonymiser.PseudonymiseAccountAsync(message.AccountId, cancellationToken));

        // Offers unconditionally: an account that no member ever joined can still have offers
        // outstanding, and one left behind is a way onto a team of an account that no longer exists.
        // Every offer of the account goes, answered or not; the residue audit counts them by AccountId.
        _dbContext.AccountMembershipOffers.RemoveRange(offers);

        // Every membership of the account goes: it is a permission to act for something that is
        // about to stop existing. A session still speaking for the account is not touched here; its
        // next refresh re-resolves the account against the memberships left (ActiveAccountResolver).
        _dbContext.AccountMemberships.RemoveRange(memberships);

        await StageLoginRemovalAsync(users, cancellationToken);

        // One SaveChangesAsync, so the logins and their credentials leave in a single transaction.
        // Split into several, a refresh presented between them could renew a session belonging to a
        // user row that had already gone.
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await RecordErasuresAsync(erasures, cancellationToken);
    }

    /// <summary>Finds the logins that lose their only way to reach anything with this account.</summary>
    /// <param name="accountId">The account being deleted.</param>
    /// <param name="memberships">Every membership of that account.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The logins to delete; empty for the ordinary case of a panel whose only login is the administrator.</returns>
    private async Task<List<User>> FindLoginsToDeleteAsync(
        Guid accountId,
        List<AccountMembership> memberships,
        CancellationToken cancellationToken)
    {
        if (memberships.Count == 0)
        {
            return [];
        }

        // A login that still holds a membership of ANOTHER account is a person who has more than one:
        // deleting it would delete the other account's way in. It survives; every other login loses its
        // only way to reach anything and goes.
        var holderIds = memberships.Select(membership => { return membership.UserId; }).ToList();
        var keptIds = await _dbContext.AccountMemberships
            .Where(membership => holderIds.Contains(membership.UserId) && membership.AccountId != accountId)
            .Select(membership => membership.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var userIds = holderIds.Where(id => { return !keptIds.Contains(id); }).ToList();

        return await _dbContext.Users
            .Where(user => userIds.Contains(user.Id))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Stages the removal of the logins and every credential hanging off them.</summary>
    /// <param name="users">The logins to delete.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the removals are staged, not saved.</returns>
    private async Task StageLoginRemovalAsync(List<User> users, CancellationToken cancellationToken)
    {
        if (users.Count == 0)
        {
            // The ordinary case in a panel whose only login is the administrator, who holds no
            // membership. Returning early keeps the cascade from issuing four empty selects per
            // deletion.
            return;
        }

        var userIds = users.Select(user => { return user.Id; }).ToList();

        // Sessions first, because a refresh token is the credential that works without the password:
        // it is the one piece of this that an attacker could already be holding.
        var sessions = await _dbContext.Sessions
            .Where(session => userIds.Contains(session.UserId))
            .ToListAsync(cancellationToken);
        _dbContext.Sessions.RemoveRange(sessions);

        var resetTokens = await _dbContext.PasswordResetTokens
            .Where(token => userIds.Contains(token.UserId))
            .ToListAsync(cancellationToken);
        _dbContext.PasswordResetTokens.RemoveRange(resetTokens);

        var invitationTokens = await _dbContext.InvitationTokens
            .Where(token => userIds.Contains(token.UserId))
            .ToListAsync(cancellationToken);
        _dbContext.InvitationTokens.RemoveRange(invitationTokens);

        var recoveryCodes = await _dbContext.RecoveryCodes
            .Where(code => userIds.Contains(code.UserId))
            .ToListAsync(cancellationToken);
        _dbContext.RecoveryCodes.RemoveRange(recoveryCodes);

        // The logins last, after everything that points at them, so a save failing part-way can
        // never leave a token or a session referencing a user row that is already gone.
        _dbContext.Users.RemoveRange(users);
    }

    /// <summary>Journals one summary entry for every erasure that matched a row.</summary>
    /// <param name="erasures">What each pseudonymisation did.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    /// <returns>Resolves once the entries are stored.</returns>
    private async Task RecordErasuresAsync(
        IReadOnlyList<AuditErasureOutcome> erasures,
        CancellationToken cancellationToken)
    {
        foreach (var erasure in erasures.Where(outcome => { return outcome.Rows > 0; }))
        {
            await _journal.RecordSystemAsync(
                IdentityAuditActions.PersonalDataErased,
                string.Create(CultureInfo.InvariantCulture, $"{erasure.Pseudonym} rows={erasure.Rows}"),
                succeeded: true,
                cancellationToken);
        }
    }
}
