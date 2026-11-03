using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Removes the panel logins of an account that is about to be deleted, and every credential those
/// logins carry (<see cref="AccountDeleting"/>).
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
/// account, so the only entity in this module carrying an <c>AccountId</c> is
/// <c>AccountMembership</c>, and it is the only one the panel's residue audit can see. The rest of
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
/// dependents of tenant rows, and a login stopped being one when its account column was dropped
/// (issue #325). The assertion that the credentials are gone is
/// <c>AccountDeletionCascadeTests</c>, against real PostgreSQL.
/// </para>
/// <para>
/// <b>What it deliberately leaves alone.</b> <c>AuditEvent</c> is the append-only journal and
/// carries the deleted user's id as an ACTOR, not as property — erasing an account's history is the
/// opposite of what an audit trail is for, and the ids of the sign-ins that happened remain the
/// operator's record of them. <c>SecurityPolicy</c> is a single server-wide row keyed by a constant
/// and belongs to no account. <c>FailedLoginByIp</c> is counting state keyed by an ADDRESS, and
/// clearing it because a tenant left would hand an attacker a way to reset their own lockout.
/// </para>
/// <para>
/// <b>No query filter is bypassed, and that is a fact about this module rather than an oversight.</b>
/// <c>AccountMembership</c> is named in <c>TenantScopeTests</c>' exemption list, and <c>User</c> and
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
/// dependents before principals, so a save that fails part-way cannot leave a token row pointing at
/// a user that is gone.
/// </para>
/// <para>
/// <b>It writes no audit entry of its own, unlike this module's request handlers.</b> The journal's
/// entries record who asked, from what address, with what client; a cascade has none of those, and
/// an entry with the panel as actor and no origin would say less than the one the Accounts module
/// already writes for the deletion this is part of. The sibling subscribers behave the same way.
/// </para>
/// </remarks>
public sealed class AccountDeletingHandler
{
    /// <summary>The Identity module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Identity module's database context.</param>
    public AccountDeletingHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Removes every membership of the account being removed, and deletes the logins left with no
    /// membership together with everything hanging off them.
    /// </summary>
    /// <param name="message">The account about to be deleted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the memberships, the orphaned logins and their credentials are gone.</returns>
    public async Task HandleAsync(AccountDeleting message, CancellationToken cancellationToken)
    {
        var memberships = await _dbContext.AccountMemberships
            .Where(membership => membership.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
        if (memberships.Count == 0)
        {
            // The ordinary case in a panel whose only login is the administrator, who holds no
            // membership. Returning early keeps the cascade from issuing four empty deletes per deletion.
            return;
        }

        // Every membership of the account goes: it is a permission to act for something that is
        // about to stop existing. A session still speaking for the account is not touched here; its
        // next refresh re-resolves the account against the memberships left (ActiveAccountResolver).
        _dbContext.AccountMemberships.RemoveRange(memberships);

        // A login that still holds a membership of ANOTHER account is a person who has more than one:
        // deleting it would delete the other account's way in. It survives; every other login loses its
        // only way to reach anything and goes.
        var holderIds = memberships.Select(membership => { return membership.UserId; }).ToList();
        var keptIds = await _dbContext.AccountMemberships
            .Where(membership => holderIds.Contains(membership.UserId) && membership.AccountId != message.AccountId)
            .Select(membership => membership.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var userIds = holderIds.Where(id => { return !keptIds.Contains(id); }).ToList();
        var users = await _dbContext.Users
            .Where(user => userIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

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

        // One SaveChangesAsync, so the logins and their credentials leave in a single transaction.
        // Split into several, a refresh presented between them could renew a session belonging to a
        // user row that had already gone.
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
