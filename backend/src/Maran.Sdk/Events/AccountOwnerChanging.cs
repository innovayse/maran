namespace Maran.Sdk.Events;

/// <summary>
/// Announced by the Accounts module immediately BEFORE a hosting account is handed to another owner (a
/// different reseller, or the administrator), so that the Identity module can end the sessions the
/// former owner holds on the account.
/// </summary>
/// <remarks>
/// <para>
/// Present tense, and it is load-bearing, for the reason <see cref="AccountSuspending"/> gives: it is
/// invoked inline before the row is written, and a subscriber that throws ABORTS the move and leaves the
/// account with its owner. That is the direction that can be repeated; an account already moved whose
/// former reseller can still act as its customer is a hole the move has already announced closed.
/// </para>
/// <para>
/// <b>What the subscriber ends, and what it leaves.</b> The view-as sessions a reseller opened on the
/// account: they exist only because that reseller owned it. It does not touch the customer's own
/// sessions (their tokens name the account, never a reseller, so the move changes nothing they can
/// do) and it does not touch an administrator's view-as session (an administrator sees every account
/// before and after).
/// </para>
/// <para>
/// The event does not name the former or the new owner: a subscriber that needed either would be
/// reaching across a schema it may not read, and the one that exists does not need them.
/// </para>
/// </remarks>
/// <param name="AccountId">
/// The account about to change hands. Every subscriber acts on its own rows carrying this value and
/// touches nothing else — this is a cascade, not an invitation to reach across a schema.
/// </param>
public sealed record AccountOwnerChanging(Guid AccountId);
