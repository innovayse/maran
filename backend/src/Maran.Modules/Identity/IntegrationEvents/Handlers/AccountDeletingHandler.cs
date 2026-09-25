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
/// <b>What it owns, established from the mapping rather than assumed.</b> <c>User</c> is the only
/// entity in this module carrying an <c>AccountId</c>, so it is the only one the panel's residue
/// audit can see. The rest of an account's identity state hangs off the USER — <c>Session</c>,
/// <c>RecoveryCode</c>, <c>PasswordResetToken</c> and <c>InvitationToken</c> are all keyed by
/// <c>UserId</c> — which is precisely the shape the residue audit is blind to: it counts rows naming
/// the account, and these name a user. A handler that dropped the <c>User</c> rows alone would
/// therefore be pronounced clean by the audit while leaving live refresh tokens, and a live
/// invitation, in the table.
/// </para>
/// <para>
/// <b>What actually guarantees the dependents go, stated honestly.</b> It is the MAPPING, not the
/// four <c>RemoveRange</c> calls below: all four tables are mapped with
/// <c>DeleteBehavior.Cascade</c> from <c>User</c>, so removing a login takes them whether this
/// handler names them or not. That was measured rather than assumed — deleting the session removal
/// from this method leaves every test in the suite green, on the in-memory provider and against real
/// PostgreSQL alike, because the cascade does the work either way. The mapping is what
/// <c>AccountCascadeTests.Every_dependent_of_a_tenant_row_is_removed_with_it</c> guards, and
/// removing the cascade there is the mutation that fails.
/// </para>
/// <para>
/// They are written out anyway, for two reasons that are about the reader rather than the row. This
/// method is the module's answer to "what do you release for an account", and an answer that names
/// only <c>User</c> would understate it by four credentials; and the removal then does not depend
/// on which provider is underneath, which matters because this module's unit tests run on the
/// in-memory one, where a foreign key is a suggestion.
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
/// <c>User</c> carries no global tenant filter — it is named in <c>TenantScopeTests</c>' exemption
/// list, because the Identity module exposes no endpoint that lists or fetches users at all — so
/// there is nothing here to ignore and no <c>IgnoreQueryFilters</c> to justify.
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

    /// <summary>Deletes every login of the account being removed, and everything hanging off it.</summary>
    /// <param name="message">The account about to be deleted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the logins and their credentials are gone.</returns>
    public async Task HandleAsync(AccountDeleting message, CancellationToken cancellationToken)
    {
        var users = await _dbContext.Users
            .Where(user => user.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
        if (users.Count == 0)
        {
            // The ordinary case in a panel whose only login is the administrator, whose AccountId is
            // null. Returning early keeps the cascade from issuing four empty deletes per deletion.
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

        // One SaveChangesAsync, so the logins and their credentials leave in a single transaction.
        // Split into several, a refresh presented between them could renew a session belonging to a
        // user row that had already gone.
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
