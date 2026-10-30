namespace Maran.Sdk.Contracts;

/// <summary>
/// Asked by the Accounts module of whichever module owns the panel logins, when an administrator
/// changes a hosting account's owner address: move the address the account's login is mailed at —
/// its invitation and its password resets — to <paramref name="Email"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A request with an answer, not an event.</b> It is INVOKED inline and its handler answers
/// <c>Result&lt;bool&gt;</c>, because the change can be refused for a reason the account owner's
/// administrator must read (the address already belongs to another login, <c>EmailTaken</c>) and
/// Accounts may not reference the module that knows it (rules/architecture.md). Exactly one handler
/// exists, in Identity; a published event could not carry that refusal back.
/// </para>
/// <para>
/// <b>An account with no login yet is a success.</b> There is no address to move, and the login is
/// later recreated from the account's own row, which by then holds the new address.
/// </para>
/// </remarks>
/// <param name="AccountId">The hosting account whose login is to follow the new address.</param>
/// <param name="Email">The new, validated owner address.</param>
public sealed record AccountLoginEmailChangeRequested(Guid AccountId, string Email);
