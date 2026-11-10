namespace Maran.Sdk.Interfaces;

/// <summary>
/// Answers one question about an account's owner, for a caller that holds no tenant scope of its own: is
/// this account, right now, owned by this reseller? Implemented by the Accounts module, which owns the
/// accounts table (rules/architecture.md "Backend: modular monolith").
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is not <see cref="IAccountDirectory"/>.</b> The directory narrows every answer to the
/// current request's caller. The one asker of this window is the bearer handler checking a view-as token,
/// which runs before the request has a principal, so the directory would answer an anonymous caller
/// nothing. This window therefore applies no caller scope and hands back a single boolean, not a
/// snapshot: a reseller's id it already holds from its own session row is all it can confirm, and it
/// discloses nothing about any other account.
/// </para>
/// <para>
/// It is read on every request a reseller's view-as token makes, so the answer is the account's owner at
/// that moment and never the owner at the moment the session was started.
/// </para>
/// </remarks>
public interface IAccountOwnership
{
    /// <summary>Reports whether the account is currently owned by the reseller.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="resellerId">The reseller whose ownership is asked about.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// True only when the account exists and its owner is that reseller. False for an account owned by
    /// another reseller or by the administrator, and for an id that does not exist.
    /// </returns>
    Task<bool> IsOwnedByResellerAsync(Guid accountId, Guid resellerId, CancellationToken cancellationToken);
}
