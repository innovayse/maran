using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// Login rate limiting: partitioned per client IP, over a sliding window
/// (rules/security.md "Rate limiting is mandatory on authentication").
///
/// The partition is the IP and nothing else, deliberately. It used to be (IP, attempted username)
/// with the username read from the <c>username</c> QUERY string — while the endpoint authenticates
/// the username in the request BODY. Those are different values, and an attacker controls both:
/// posting a body naming the real account with a random query value on every request landed each
/// attempt in a fresh partition, which is unlimited guesses against one account from one address.
/// A limiter that can be given a new bucket by the caller is not a limiter.
///
/// The resolver runs before model binding and must not read the body, so the attempted username
/// is not available to it at all. Per-account protection therefore does not belong here; it
/// belongs on the user row, as failed-attempt state the handler updates, and is named in the
/// plan's residual risks until the settings module makes the policy configurable.
/// </summary>
public static class LoginRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.Login;

    /// <summary>
    /// Registers the policy on <paramref name="options"/>. Every attempt from one address shares
    /// one budget of <see cref="RateLimitOptions.LoginMaxAttempts"/>, whatever account it names,
    /// so trying many usernames from one address is bounded exactly as trying one is. The cost is
    /// that callers behind a shared address share a budget; the production numbers (5 attempts per
    /// 300 seconds) leave room for people who mistype, and the alternative — a key the caller can
    /// change at will — bounds nothing.
    /// </summary>
    /// <param name="options">The rate limiter options to add this policy to.</param>
    /// <param name="rateLimitOptions">Configured attempt count, window, and lockout duration.</param>
    public static void Configure(RateLimiterOptions options, RateLimitOptions rateLimitOptions)
    {
        options.AddPolicy(Name, context =>
        {
            var partitionKey = BuildPartitionKey(context);

            return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ =>
            {
                return new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.LoginMaxAttempts,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.LoginLockoutSeconds),
                    SegmentsPerWindow = Math.Max(1, rateLimitOptions.LoginLockoutSeconds / Math.Max(1, rateLimitOptions.LoginWindowSeconds)),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                };
            });
        });
    }

    /// <summary>
    /// Builds the partition key: the caller's address, or <c>unknown</c> when there is none.
    /// Nothing from the request is mixed in — see the type's remarks for why a caller-supplied
    /// component made the limit unenforceable.
    /// </summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>The partition every attempt from this address shares.</returns>
    private static string BuildPartitionKey(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
