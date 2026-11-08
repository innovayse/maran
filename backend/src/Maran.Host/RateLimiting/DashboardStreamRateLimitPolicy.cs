using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// Bounds how many live dashboard streams one signed-in user may hold open at the same time. A
/// concurrency limiter partitioned by caller, refusing rather than queueing (issue #199).
/// </summary>
/// <remarks>
/// Built exactly as <see cref="SiteLogStreamRateLimitPolicy"/> is, and for the same reason: a stream is
/// not a request that ends, so a fixed window would bound how fast streams are OPENED and never how
/// many are open, while a concurrency limiter's lease is returned when the response completes.
/// <c>QueueLimit = 0</c> answers 429 at once; queueing would hold the connection open waiting for the
/// very thing being rationed.
///
/// Only half of the bound is here. The other half — how many streams the whole PROCESS holds — is
/// <see cref="Dashboard.DashboardStreamGate"/>, because one policy yields one partition and the process
/// limit is a different partition entirely.
/// </remarks>
public static class DashboardStreamRateLimitPolicy
{
    /// <summary>The policy name the endpoint enables.</summary>
    public const string Name = RateLimitPolicies.DashboardStream;

    /// <summary>Registers the policy on <paramref name="options"/>.</summary>
    /// <param name="options">The rate limiter options to add this policy to.</param>
    /// <param name="streamOptions">Configured number of concurrent streams per user.</param>
    public static void Configure(RateLimiterOptions options, DashboardStreamOptions streamOptions)
    {
        options.AddPolicy(Name, context =>
        {
            return RateLimitPartition.GetConcurrencyLimiter(
                RateLimitPartitionKey.For(context),
                _ =>
                {
                    return new ConcurrencyLimiterOptions
                    {
                        PermitLimit = streamOptions.PerUserStreamLimit,
                        QueueLimit = 0,
                    };
                });
        });
    }
}
