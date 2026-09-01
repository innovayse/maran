using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// Bounds how many site-log tails one account may hold open at the same time. A concurrency
/// limiter, partitioned by account, refusing rather than queueing.
/// </summary>
/// <remarks>
/// This exists because the general <see cref="ApiRateLimitPolicy"/> cannot express it and no other
/// limit in the panel comes close. A tail is not a request that ends: it is a connection held for as
/// long as an operator watches, and on the far side of it the root daemon holds one blocking thread
/// for the life of the stream, out of a pool shared by EVERY agent operation. So a single customer
/// opening tails on sites they legitimately own can exhaust that pool and leave every other tenant's
/// account creation, certificate install and file operation queued behind it. The agent cannot stop
/// that — it has no notion of a customer — and its per-stream guards are already correct, so the
/// panel is the only place the aggregate can be bounded.
///
/// A fixed window is the wrong primitive and this is worth stating plainly: its lease returns no
/// permit when the request ends, so it limits how fast tails are OPENED and never how many are open.
/// A concurrency limiter's lease is returned on disposal, which is exactly the accounting a stream
/// needs.
///
/// <c>QueueLimit = 0</c>: a caller over the limit is told so immediately with 429. Queueing would
/// hold the connection open waiting for a permit, which is the resource being rationed.
///
/// Partitioned by ACCOUNT, not by user: the resource is consumed on behalf of a hosting account, and
/// an account with five panel users would otherwise get five times the budget.
/// </remarks>
public static class SiteLogStreamRateLimitPolicy
{
    /// <summary>The policy name the endpoint enables with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.SiteLogs;

    /// <summary>Registers the policy on <paramref name="options"/>.</summary>
    /// <param name="options">The rate limiter options to add this policy to.</param>
    /// <param name="rateLimitOptions">Configured number of concurrent streams per account.</param>
    public static void Configure(RateLimiterOptions options, RateLimitOptions rateLimitOptions)
    {
        options.AddPolicy(Name, context =>
        {
            return RateLimitPartition.GetConcurrencyLimiter(
                RateLimitPartitionKey.For(context),
                _ =>
                {
                    return new ConcurrencyLimiterOptions
                    {
                        PermitLimit = rateLimitOptions.SiteLogConcurrentStreamLimit,
                        QueueLimit = 0,
                    };
                });
        });
    }
}
