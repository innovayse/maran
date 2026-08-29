using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// Login rate limiting: partitioned per (client IP, attempted username), a tight sliding window,
/// and a progressive lockout — once the window's attempt budget is exhausted the caller stays
/// blocked until the (longer) lockout window elapses, rather than regaining permits on the next
/// short window tick. No authentication endpoint exists yet; this policy is registered so the
/// first one to ship (rules/security.md "Rate limiting is mandatory on authentication") only has
/// to add <c>[EnableRateLimiting(LoginRateLimitPolicy.Name)]</c>.
/// </summary>
public static class LoginRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = "login";

    /// <summary>
    /// Registers the policy on <paramref name="options"/>. Partition key is (IP, username): a
    /// shared IP does not lock out other users, and a distributed attempt on one username from
    /// many IPs is still bounded by <see cref="RateLimitOptions.LoginMaxAttempts"/> only within
    /// that pair, matching how login abuse actually happens (credential stuffing on one account,
    /// or brute force from one address).
    /// </summary>
    /// <param name="options">The rate limiter options to add this policy to.</param>
    /// <param name="rateLimitOptions">Configured attempt count, window, and lockout duration.</param>
    public static void Configure(RateLimiterOptions options, RateLimitOptions rateLimitOptions)
    {
        options.AddPolicy(Name, context =>
        {
            var partitionKey = BuildPartitionKey(context);

            return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.LoginMaxAttempts,
                Window = TimeSpan.FromSeconds(rateLimitOptions.LoginLockoutSeconds),
                SegmentsPerWindow = Math.Max(1, rateLimitOptions.LoginLockoutSeconds / Math.Max(1, rateLimitOptions.LoginWindowSeconds)),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });
    }

    /// <summary>
    /// Builds the (IP, username) partition key. The username comes from a <c>username</c> query
    /// or route value when present — the partition resolver runs synchronously and MUST NOT read
    /// the request body, so the login endpoint (when it ships) is expected to surface the
    /// attempted username that way, e.g. via a route-bound value or by reading the body itself
    /// before the limiter short-circuits it. Requests without one still partition by IP alone, so
    /// an attempt this policy cannot identify a username for is still bounded.
    /// </summary>
    /// <param name="context">The current HTTP request.</param>
    private static string BuildPartitionKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var username = context.Request.Query.TryGetValue("username", out var value) ? value.ToString() : "unknown";

        return $"{ip}:{username}";
    }
}
