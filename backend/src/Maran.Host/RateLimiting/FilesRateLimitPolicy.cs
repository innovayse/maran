using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// The file-manager limit: its own fixed window, partitioned per hosting account
/// (rules/security.md "Rate limiting is mandatory ... and any expensive operation").
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own policy rather than a bigger number on <see cref="ApiRateLimitPolicy"/>.</b> A file
/// manager sends a request per folder opened and per search pause, which the general budget — sized for
/// reading screens — would throttle during ordinary browsing; and the budget is not what bounds the
/// expensive part. A transfer holds an agent stream for as long as it lasts, which a request count
/// cannot express, so transfers are bounded by the module's own concurrency slots.
/// </para>
/// <para>
/// Keyed by the hosting ACCOUNT, through the shared <see cref="RateLimitPartitionKey"/>, so an account
/// with several panel users does not multiply its budget. The queue is zero: a caller over the limit
/// is refused at once rather than parked.
/// </para>
/// </remarks>
public static class FilesRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.Files;

    /// <summary>Registers the policy on <paramref name="options"/>.</summary>
    /// <param name="options">The rate limiter options to add this policy to.</param>
    /// <param name="rateLimitOptions">Configured request count and window.</param>
    public static void Configure(RateLimiterOptions options, RateLimitOptions rateLimitOptions)
    {
        ArgumentNullException.ThrowIfNull(rateLimitOptions);

        options.AddPolicy(Name, context =>
        {
            return RateLimitPartition.GetFixedWindowLimiter(RateLimitPartitionKey.For(context), _ =>
            {
                return new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.FilesMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.FilesWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                };
            });
        });
    }
}
