using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Sdk.Events;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Suspends every membership of an account that is about to be suspended, and ends the sessions
/// that were speaking for it (<see cref="AccountSuspending"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It acts on the account's memberships, not on a login.</b> A login can hold several accounts,
/// so suspending one must close that account's door and leave the others open: each membership of
/// the account (the owner's and every added member's) becomes
/// <see cref="MembershipState.Suspended"/>, and sign-in, refresh, account switching and the
/// feature gate all read that state. The login itself is untouched. Added members used to keep
/// working in a suspended account because only the owner's login was found; they are memberships
/// of the account like the owner's, and are suspended with it.
/// </para>
/// <para>
/// <b>Closing the door is not enough on its own.</b> Only the NEXT sign-in or refresh reads the
/// state; an access token issued before the suspension keeps authenticating for the rest of its own
/// lifetime unless the sessions behind it are revoked here. Sessions that were speaking for THIS
/// account end; a session of the same person speaking for another, still active, account is left
/// alone. A person whose every account is now suspended has no such session to keep: their refresh
/// is refused on its own.
/// </para>
/// <para>
/// <b>An invited owner is not touched beyond the membership.</b> Suspending an account whose owner
/// never accepted their invitation must not consume the invitation: the login stays
/// <see cref="UserLifecycle.Invited"/>, and the suspended membership is what keeps an invitation
/// accepted meanwhile from producing a way in.
/// </para>
/// <para>
/// <b>Repeating it is harmless.</b> A membership already suspended stays so, and an account that
/// has no membership at all (its login was already removed) has nothing to do.
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

    /// <summary>Suspends the account's memberships and revokes the sessions speaking for the account.</summary>
    /// <param name="message">The account about to be suspended.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Resolves once the memberships are suspended and the sessions are gone.</returns>
    public async Task HandleAsync(AccountSuspending message, CancellationToken cancellationToken)
    {
        var memberships = await _dbContext.AccountMemberships
            .Where(membership => membership.AccountId == message.AccountId)
            .ToListAsync(cancellationToken);
        if (memberships.Count == 0)
        {
            // No login acts for this account: nothing to close. See the type's remarks.
            return;
        }

        foreach (var membership in memberships)
        {
            membership.Suspend();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Revoking the sessions is the half that matters: a suspension that only blocks the NEXT
        // sign-in leaves whoever is already signed in working normally until their access token
        // expires, which is exactly the window a suspension exists to close.
        await _sessionService.RevokeSpeakingForAccountAsync(
            message.AccountId, SessionRevocationReason.AccountSuspended, cancellationToken);
    }
}
