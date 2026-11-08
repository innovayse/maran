using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// The named <see cref="RateLimitPolicies.Dns"/> policy: a fixed window per caller, applied to every DNS
/// endpoint. Every accepted edit re-renders a whole zone on the root agent and reloads it in the name
/// server, so the budget is its own and is not spent on, or by, the reading screens that share
/// <see cref="RateLimitPolicies.Api"/>.
/// </summary>
public static class DnsRateLimitPolicy
{
    /// <summary>The policy name controllers reference with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.Dns;

    /// <summary>Registers the policy on the limiter options.</summary>
    /// <param name="options">The limiter options being built.</param>
    /// <param name="rateLimitOptions">The validated numbers, so the policy holds none of its own.</param>
    public static void Configure(RateLimiterOptions options, RateLimitOptions rateLimitOptions)
    {
        ArgumentNullException.ThrowIfNull(rateLimitOptions);

        options.AddPolicy(Name, context =>
        {
            return RateLimitPartition.GetFixedWindowLimiter(RateLimitPartitionKey.For(context), _ =>
            {
                return new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.DnsMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.DnsWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                };
            });
        });
    }
}
