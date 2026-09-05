using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
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
    public const string Name = RateLimitPolicies.Api;

    /// <summary>
    /// Registers the policy on <paramref name="options"/>. Partitioned by the caller's account
    /// (see <see cref="RateLimitPartitionKey"/>), so one account cannot widen its budget by opening
    /// more connections or by adding more panel users.
    /// </summary>
    /// <param name="options">The rate limiter options to add this policy to.</param>
    /// <param name="rateLimitOptions">Configured permit count and window.</param>
    public static void Configure(RateLimiterOptions options, RateLimitOptions rateLimitOptions)
    {
        options.AddPolicy(Name, context =>
        {
            var partitionKey = RateLimitPartitionKey.For(context);

            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
            {
                return new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.ApiPermitLimit,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.ApiWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                };
            });
        });
    }
}
