using Maran.Sdk.Contracts;

namespace Maran.Host.RateLimiting;

/// <summary>
/// Builds the key a rate-limit partition is measured against: the caller's hosting account, then
/// their panel user, then their address.
/// </summary>
/// <remarks>
/// One implementation, shared by every policy that limits an authenticated caller, because the two
/// that existed before disagreed with themselves. <see cref="ApiRateLimitPolicy"/> named its key
/// "account:" and put a USER id in it, and read that id from <c>ClaimTypes.NameIdentifier</c> — a
/// claim the panel never issues, since it disables inbound claim mapping and writes the registered
/// <c>sub</c> name instead. The value was therefore always absent and every authenticated caller
/// fell through to the per-IP branch, so every customer behind one NAT shared a single budget.
///
/// The order is what makes the limit mean something: an account is the unit a resource is consumed
/// on behalf of, so an account with five panel users must not get five times the budget. A panel
/// administrator owns no account and is keyed by user. An anonymous caller has neither and is keyed
/// by address, which is all that is known about them.
/// </remarks>
public static class RateLimitPartitionKey
{
    /// <summary>Builds the partition key for one request.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>The key, prefixed with which of the three identities it names.</returns>
    public static string For(HttpContext context)
    {
        var accountId = context.User.FindFirst(PanelClaimTypes.AccountId)?.Value;
        if (!string.IsNullOrEmpty(accountId))
        {
            return $"account:{accountId}";
        }

        var userId = context.User.FindFirst(PanelClaimTypes.UserId)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}
