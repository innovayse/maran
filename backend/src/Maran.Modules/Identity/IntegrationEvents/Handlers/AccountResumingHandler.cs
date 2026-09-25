using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Lets the panel login of a reactivated account sign in again (<see cref="AccountResuming"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It does not restore sessions, and that is the law rather than an omission.</b>
/// <see cref="AccountSuspendingHandler"/> revoked them, and a revoked session is gone the same way a
/// spent one is — there is nothing here to give back. The owner signs in again and is issued a fresh
/// one, exactly as they would after signing out anywhere else.
/// </para>
/// <para>
/// <b>The ordinary case is a repeat rather than a genuine miss</b>, for the reason
/// <see cref="AccountSuspendingHandler"/>'s remarks give: only the administrator can be without a
/// login, and the administrator is never named by this event.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the reactivation with the account left suspended — the recoverable direction here
/// too, for the same reason every sibling subscriber gives.
/// </para>
/// <para>
/// <b>It writes no audit entry of its own</b>, for the reason <see cref="AccountDeletingHandler"/>
/// gives: the journal's entries record who asked and from where, and a cascade has none of those.
/// </para>
/// </remarks>
public sealed class AccountResumingHandler
{
    /// <summary>The Identity module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Identity module's database context.</param>
    public AccountResumingHandler(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Resumes the account's login, leaving its sessions exactly as the suspension left them.</summary>
    /// <param name="message">The account about to be reactivated.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the login may sign in again.</returns>
    public async Task HandleAsync(AccountResuming message, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.AccountId == message.AccountId, cancellationToken);
        if (user is null)
        {
            // No login owns this account. See AccountSuspendingHandler's remarks.
            return;
        }

        user.Resume();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
