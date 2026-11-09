namespace Maran.Modules.Identity.Models;

/// <summary>
/// A reseller's scope: the entries stamped with one of the accounts it owns, and the entries its own
/// login made. Nothing else of the journal is visible to it, however the request is phrased.
/// </summary>
/// <param name="OwnedAccountIds">The ids of the accounts the reseller owns, read from the Accounts module.</param>
/// <param name="LoginUserId">The panel user id of the reseller login that is reading.</param>
public sealed record ResellerEntriesScope(IReadOnlyCollection<Guid> OwnedAccountIds, Guid LoginUserId) : AuditEventScope
{
    /// <summary>Whether <paramref name="accountId"/> is one of the accounts the reseller owns.</summary>
    /// <param name="accountId">The account asked about.</param>
    /// <returns>True when the reseller owns it.</returns>
    public bool Owns(Guid accountId)
    {
        return OwnedAccountIds.Contains(accountId);
    }
}
