using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Removes the login of a reseller that is about to be deleted, and every credential that login
/// carries (<see cref="ResellerDeleting"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It releases a WAY IN, as <see cref="AccountDeletingHandler"/> does.</b> A login left behind by
/// a deleted reseller is a username and a password that still authenticate, and a refresh token left
/// behind is a session that works without anybody typing that password. Everything that hangs off the
/// login is keyed by its user id — <c>Session</c>, <c>RecoveryCode</c>, <c>PasswordResetToken</c> and
/// <c>InvitationToken</c> — so the removal names them rather than trusting the foreign keys' cascade
/// (the module's unit tests run on the in-memory provider, where a foreign key is a suggestion).
/// Sessions an administrator opened by viewing as the reseller are the reseller login's sessions and
/// go with them.
/// </para>
/// <para>
/// <b>What it leaves alone</b>, for the reasons <see cref="AccountDeletingHandler"/> gives: the
/// append-only <c>AuditEvent</c> journal, the single <c>SecurityPolicy</c> row, and the failed-sign-in
/// counters keyed by address.
/// </para>
/// <para>
/// <b>It never decides what becomes of the reseller's customers.</b> A reseller that still owns
/// accounts is refused by the Accounts module before this is announced, so no customer login is in
/// reach here, and this handler touches only the login carrying the reseller id.
/// </para>
/// <para>
/// <b>Repeating it is harmless</b> (no login, nothing to do), and <b>the failure is not swallowed</b>:
/// anything thrown here propagates to the Accounts handler, which abandons the deletion with the
/// reseller intact. The order is dependents before the principal, in one save, so a failure part-way
/// cannot leave a token pointing at a user that is gone.
/// </para>
/// <para>
/// <b>It writes no audit entry of its own</b>, and bypasses no query filter: no entity of this module
/// carries a reseller id the tenant census would follow, and the module has no filter to ignore.
/// </para>
/// </remarks>
public sealed class ResellerDeletingHandler
{
    /// <summary>The Identity module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Identity module's database context.</param>
    public ResellerDeletingHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Deletes the reseller's login together with everything hanging off it.</summary>
    /// <param name="message">The reseller about to be deleted.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the login and its credentials are gone.</returns>
    public async Task HandleAsync(ResellerDeleting message, CancellationToken cancellationToken)
    {
        var login = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.ResellerId == message.ResellerId, cancellationToken);
        if (login is null)
        {
            return;
        }

        // Sessions first, because a refresh token is the credential that works without the password.
        var sessions = await _dbContext.Sessions
            .Where(session => session.UserId == login.Id)
            .ToListAsync(cancellationToken);
        _dbContext.Sessions.RemoveRange(sessions);

        var resetTokens = await _dbContext.PasswordResetTokens
            .Where(token => token.UserId == login.Id)
            .ToListAsync(cancellationToken);
        _dbContext.PasswordResetTokens.RemoveRange(resetTokens);

        var invitationTokens = await _dbContext.InvitationTokens
            .Where(token => token.UserId == login.Id)
            .ToListAsync(cancellationToken);
        _dbContext.InvitationTokens.RemoveRange(invitationTokens);

        var recoveryCodes = await _dbContext.RecoveryCodes
            .Where(code => code.UserId == login.Id)
            .ToListAsync(cancellationToken);
        _dbContext.RecoveryCodes.RemoveRange(recoveryCodes);

        // The login last, after everything that points at it.
        _dbContext.Users.Remove(login);

        // One save: the login and its credentials leave in a single transaction.
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
