using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Interfaces;
using Maran.Sdk.Events;

namespace Maran.Modules.Identity.IntegrationEvents.Handlers;

/// <summary>
/// Ends the view-as sessions a reseller holds on an account that is about to change hands
/// (<see cref="AccountOwnerChanging"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>It closes a way in the former owner would otherwise keep.</b> A reseller's view-as session is a
/// customer token for the account's owner login with the reseller as its operator, valid for the
/// view-as lifetime and checked against its session row on every request. The only reason it may exist
/// is that the reseller owns the account; once the account is another owner's, a row left live is a
/// former reseller operating a customer it no longer has any claim on.
/// </para>
/// <para>
/// <b>What it leaves alone, deliberately.</b> The customer's own sessions: their tokens name the account
/// and no reseller, so the move changes nothing they can do, and signing a paying customer out for a
/// billing re-assignment would be damage with no security gain. An administrator's view-as session on
/// the account: an administrator sees every account before the move and after it. Memberships and
/// logins: they belong to the account, which is the same account.
/// </para>
/// <para>
/// <b>Repeating it is harmless</b> (no such session, nothing to do), and <b>the failure is not
/// swallowed</b>: anything thrown here propagates to the Accounts handler, which abandons the move with
/// the account left with its owner — the recoverable direction, for the reason every sibling
/// subscriber gives. It writes no audit entry of its own: the Accounts module journals the move, and a
/// cascade has no caller of its own to record.
/// </para>
/// </remarks>
public sealed class AccountOwnerChangingHandler
{
    /// <summary>Ends the sessions.</summary>
    private readonly ISessionService _sessionService;

    /// <summary>Creates the handler.</summary>
    /// <param name="sessionService">Ends the sessions.</param>
    public AccountOwnerChangingHandler(ISessionService sessionService)
    {
        _sessionService = sessionService;
    }

    /// <summary>Ends the reseller view-as sessions on the account.</summary>
    /// <param name="message">The announcement naming the account about to change hands.</param>
    /// <param name="cancellationToken">Cancellation token for the work.</param>
    /// <returns>Resolves once the sessions are revoked.</returns>
    public async Task HandleAsync(AccountOwnerChanging message, CancellationToken cancellationToken)
    {
        await _sessionService.RevokeResellerViewAsForAccountAsync(
            message.AccountId, SessionRevocationReason.AccountTransferred, cancellationToken);
    }
}
