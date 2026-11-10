using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// Session-refresh rate limiting: its own bucket, partitioned per client address, for the anonymous
/// endpoints that act on the refresh cookie (<c>refresh</c> and <c>logout</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>What an unlimited endpoint spends.</b> Every request that carries a cookie costs a database
/// lookup of the token digest, and a presented spent token additionally triggers family revocation
/// writes, so an anonymous caller at wire speed is load on the sign-in surface and an unthrottled
/// probe of the reuse-detection path. Guessing a 256-bit token is not the threat; load is.
/// </para>
/// <para>
/// <b>Partitioned by the address alone, and why not by token family as well.</b> A named policy
/// yields ONE partition per request, and the family a cookie belongs to is known only after a database
/// lookup, which is the very cost being bounded. A key derived from the cookie value would let a
/// caller change it at will (a random cookie per request is a fresh bucket each time), so it would
/// bound nothing. The family is bounded where it is known: presenting a spent token revokes the whole
/// family, after which every further presentation is a plain refusal.
/// </para>
/// <para>
/// Fixed window, not sliding, because a refused refresh is cheap to retry and the fixed window
/// publishes <c>Retry-After</c> to the shared rejection writer, which a sliding window does not.
/// </para>
/// </remarks>
public static class SessionRefreshRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.SessionRefresh;

    /// <summary>Registers the policy on <paramref name="options"/>.</summary>
    /// <param name="options">The rate limiter options to add this policy to.</param>
    /// <param name="rateLimitOptions">Configured request count and window.</param>
    public static void Configure(RateLimiterOptions options, RateLimitOptions rateLimitOptions)
    {
        options.AddPolicy(Name, context =>
        {
            return RateLimitPartition.GetFixedWindowLimiter(BuildPartitionKey(context), _ =>
            {
                return new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.SessionRefreshMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.SessionRefreshWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                };
            });
        });
    }

    /// <summary>Builds the partition key: the caller's address in the panel's canonical spelling.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>The partition every refresh and logout request from this address shares.</returns>
    private static string BuildPartitionKey(HttpContext context)
    {
        return ClientAddress.Of(context.Connection.RemoteIpAddress);
    }
}
