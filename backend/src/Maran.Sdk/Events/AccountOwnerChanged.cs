namespace Maran.Sdk.Events;

/// <summary>
/// Announced by the Accounts module AFTER a hosting account has been handed to another owner (a different
/// reseller, or the administrator), so that the Identity module can end the sessions the former owner
/// still holds on the account.
/// </summary>
/// <remarks>
/// <para>
/// <b>Past tense, after the commit, and it is not what keeps the former owner out.</b> An earlier
/// version announced the move BEFORE the row was written and revoked then, which left a window: the
/// former reseller still owned the account until the save, so it could open a fresh view-as session
/// after the revocation and keep it for the view-as lifetime (issue #460). What closes the way in is the
/// ownership check every view-as request makes against the account's current owner (Identity's
/// <c>ViewAsSessionVerifier</c>); this announcement only ends the now-useless rows, so a subscriber's
/// failure is logged and never fails or undoes a move that is already committed.
/// </para>
/// <para>
/// <b>What the subscriber ends, and what it leaves.</b> The view-as sessions a reseller opened on the
/// account. It does not touch the customer's own sessions (their tokens name the account, never a
/// reseller) and it does not touch an administrator's view-as session (an administrator sees every
/// account before and after).
/// </para>
/// <para>
/// The event does not name the former or the new owner: a subscriber that needed either would be
/// reaching across a schema it may not read, and the one that exists does not need them.
/// </para>
/// </remarks>
/// <param name="AccountId">
/// The account that changed hands. Every subscriber acts on its own rows carrying this value and
/// touches nothing else.
/// </param>
public sealed record AccountOwnerChanged(Guid AccountId);
