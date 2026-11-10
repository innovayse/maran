using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Sdk.Events;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Ends the view-as sessions a reseller holds on an account that has changed hands
/// (<see cref="AccountOwnerChanged"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It tidies; it is not the control.</b> A reseller's view-as session is a customer token for the
/// account's owner login with the reseller as its operator. The control that keeps a former owner out is
/// <c>ViewAsSessionVerifier</c>, which asks the account's CURRENT owner on every request, so a session
/// opened at any moment, including one racing the move, is refused from the commit on (issue #460).
/// This handler ends the rows that check has made useless, after the commit.
/// </para>
/// <para>
/// <b>What it leaves alone, deliberately.</b> The customer's own sessions: their tokens name the account
/// and no reseller, so the move changes nothing they can do, and signing a paying customer out for a
/// billing re-assignment would be damage with no security gain. An administrator's view-as session on
/// the account: an administrator sees every account before the move and after it. Memberships and
/// logins: they belong to the account, which is the same account.
/// </para>
/// <para>
/// <b>Repeating it is harmless</b> (no such session, nothing to do), and a failure here is
/// logged by the Accounts announcer and never undoes the committed move. It writes no audit entry of its own: the Accounts module journals the move, and a
/// cascade has no caller of its own to record.
/// </para>
/// </remarks>
public sealed class AccountOwnerChangedHandler
{
    /// <summary>Ends the sessions.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Creates the handler.</summary>
    /// <param name="sessionService">Ends the sessions.</param>
    public AccountOwnerChangedHandler(ISessionService sessionService)
    {
        _sessionService = sessionService;
    }

    /// <summary>Ends the reseller view-as sessions on the account.</summary>
    /// <param name="message">The announcement naming the account about to change hands.</param>
    /// <param name="cancellationToken">Cancellation token for the work.</param>
    /// <returns>Resolves once the sessions are revoked.</returns>
    public async Task HandleAsync(AccountOwnerChanged message, CancellationToken cancellationToken)
    {
        await _sessionService.RevokeResellerViewAsForAccountAsync(
            message.AccountId, SessionRevocationReason.AccountTransferred, cancellationToken);
    }
}
