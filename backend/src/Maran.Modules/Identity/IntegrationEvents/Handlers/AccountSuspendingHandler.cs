using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Blocks the panel login of an account that is about to be suspended, and ends every session
/// already open on it (<see cref="AccountSuspending"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Closing the door is not enough on its own.</b> Marking the login <see cref="UserState.Suspended"/>
/// only stops the NEXT sign-in; an access token issued before the suspension keeps authenticating
/// for the rest of its own lifetime unless the sessions behind it are revoked here. That is the half
/// of a suspension that actually matters — a customer already signed in must not go on working
/// normally until their token happens to expire.
/// </para>
/// <para>
/// <b>An invited login is left invited, by <see cref="Domain.Entities.User.Suspend"/>'s own rule.</b>
/// Suspending an account whose owner never accepted their invitation must not silently consume it;
/// such a login cannot sign in anyway, and it holds no session to revoke.
/// </para>
/// <para>
/// <b>The ordinary case is a repeat rather than a genuine miss.</b> An account with no login at all
/// is possible only for the administrator, whose <c>AccountId</c> is null and who is therefore never
/// named by this event — <see cref="AccountSuspending.AccountId"/> always names a hosting account,
/// and <c>UX_Users_AccountId</c> guarantees at most one login owns it. A missing row here means this
/// account's login was already removed, not that one was never issued.
/// </para>
/// <para>
/// <b>The failure is not swallowed.</b> Anything thrown here propagates to the Accounts handler,
/// which abandons the suspension with the account left active — the recoverable direction, for the
/// same reason every sibling subscriber gives.
/// </para>
/// <para>
/// <b>It writes no audit entry of its own</b>, for the reason <see cref="AccountDeletingHandler"/>
/// gives: the journal's entries record who asked and from where, and a cascade has none of those.
/// </para>
/// </remarks>
public sealed class AccountSuspendingHandler
{
    /// <summary>The Identity module's database context.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Ends the login's live sessions.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Identity module's database context.</param>
    /// <param name="sessionService">Ends the login's live sessions.</param>
    public AccountSuspendingHandler(IdentityDbContext dbContext, ISessionService sessionService)
    {
        _dbContext = dbContext;
        _sessionService = sessionService;
    }

    /// <summary>Suspends the account's login and revokes every session already issued to it.</summary>
    /// <param name="message">The account about to be suspended.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the login is blocked and its sessions are gone.</returns>
    public async Task HandleAsync(AccountSuspending message, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .SingleOrDefaultAsync(candidate => candidate.AccountId == message.AccountId, cancellationToken);
        if (user is null)
        {
            // No login owns this account. See the type's remarks: only the administrator, who is
            // never named here, can be without one.
            return;
        }

        user.Suspend();
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Revoking the sessions is the half that matters: a suspension that only blocks the NEXT
        // sign-in leaves whoever is already signed in working normally until their access token
        // expires, which is exactly the window a suspension exists to close.
        await _sessionService.RevokeAllAsync(user.Id, SessionRevocationReason.AccountSuspended, cancellationToken);
    }
}
