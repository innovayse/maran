using System.Security.Claims;
using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// General API rate limiting: a fixed window of requests per authenticated account, falling back
/// to per-IP when the caller is anonymous. Modules apply it with
/// <c>[EnableRateLimiting(ApiRateLimitPolicy.Name)]</c> on their controllers
/// (rules/csharp.md "Controller shape is fixed").
/// </summary>
public static class ApiRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = "api";

    /// <summary>Claim type the panel's authentication (once it ships) stores the user id under.</summary>
    private const string UserIdClaimType = ClaimTypes.NameIdentifier;

    /// <summary>
    /// Registers the policy on <paramref name="options"/>. Partitioning by account rather than
    /// connection means one account cannot exhaust its budget faster by opening more connections,
    /// and an anonymous caller (no user id claim yet, since Plan 2 has not shipped authentication)
    /// falls back to IP so the limiter is still meaningful today.
    /// </summary>
    /// <param name="options">The rate limiter options to add this policy to.</param>
    /// <param name="rateLimitOptions">Configured permit count and window.</param>
    public static void Configure(RateLimiterOptions options, RateLimitOptions rateLimitOptions)
    {
        options.AddPolicy(Name, context =>
        {
            var partitionKey = BuildPartitionKey(context);

            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.ApiPermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitOptions.ApiWindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });
    }

    /// <summary>Builds the partition key: the authenticated account id, or the caller's IP when anonymous.</summary>
    /// <param name="context">The current HTTP request.</param>
    private static string BuildPartitionKey(HttpContext context)
    {
        var userId = context.User.FindFirst(UserIdClaimType)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            return $"account:{userId}";
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}";
    }
}
