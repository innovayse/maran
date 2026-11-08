namespace Maran.Sdk.Contracts;

/// <summary>
/// Asked by the Accounts module of whichever module owns the panel logins, when an administrator
/// renames a reseller or changes its contact address: the reseller's login follows, so it signs in
/// with the new name and its invitation and password resets are mailed at the new address.
/// </summary>
/// <remarks>
/// <para>
/// <b>A request with an answer, not an event</b>, for the reason
/// <see cref="AccountLoginEmailChangeRequested"/> gives: the change can be refused for a reason the
/// administrator must read (<c>UsernameTaken</c>, <c>EmailTaken</c>), and Accounts may not reference
/// the module that knows it (rules/architecture.md). It is INVOKED inline; exactly one handler
/// exists, in Identity.
/// </para>
/// <para>
/// <b>A reseller with no login yet is a success</b>: its issuance failed and has not been repeated,
/// so there is nothing to move. Repeating the request converges: a login already carrying the name
/// and address is left as it is.
/// </para>
/// </remarks>
/// <param name="ResellerId">The reseller whose login is to follow.</param>
/// <param name="Username">The reseller's name after the change: its login name.</param>
/// <param name="Email">The reseller's contact address after the change.</param>
public sealed record ResellerLoginChangeRequested(Guid ResellerId, string Username, string Email);
