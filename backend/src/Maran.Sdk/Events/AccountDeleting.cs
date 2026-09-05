namespace Maran.Sdk.Events;

/// <summary>
/// Announced by the Accounts module immediately BEFORE a hosting account is removed, so that every
/// module holding rows against it can take them away first.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an Sdk event and not a call.</b> The Databases and Sftp modules own rows keyed by
/// <see cref="AccountId"/>, and the Accounts module may not reference either of them
/// (rules/architecture.md "Backend: modular monolith", enforced by
/// <c>ModuleIsolationTests</c>). The message is therefore declared here, in the contract surface
/// every module already depends on, and each module subscribes to it — which is also what lets a
/// paid marketplace module clean up after itself without the open code knowing it exists.
/// </para>
/// <para>
/// <b>Present tense, and it is load-bearing.</b> This is <c>AccountDeleting</c>, not
/// <c>AccountDeleted</c>: it is invoked inline, before anything has been removed, and a handler
/// that throws ABORTS THE DELETION. A past-tense notification would mean the account was already
/// gone by the time a subscriber failed, which is the one outcome this whole cascade exists to
/// prevent — rows and host resources nothing in the panel points at any more.
/// </para>
/// <para>
/// <b>What it is not.</b> It is not the thing that cleans up the HOST. The agent's own account
/// deletion drops the databases, revokes the SFTP logins and takes the jail's bind mount down,
/// asking the machine what is there rather than being handed a list — because a list can only
/// describe what the panel remembers creating. This event removes the panel's ROWS, which is a
/// different job with a different failure mode: a row left behind is a customer's database shown in
/// a panel that no longer has an account for it.
/// </para>
/// </remarks>
/// <param name="AccountId">
/// The account about to be removed. Every subscriber deletes its own rows carrying this value and
/// touches nothing else — this is a cascade, not an invitation to reach across a schema.
/// </param>
/// <param name="Username">
/// The account's Linux system user name, carried so a subscriber can journal what it removed in
/// terms an operator will recognise afterwards. The Accounts row is gone by then, so a subscriber
/// that wanted to look it up could not.
/// </param>
public sealed record AccountDeleting(Guid AccountId, string Username);
