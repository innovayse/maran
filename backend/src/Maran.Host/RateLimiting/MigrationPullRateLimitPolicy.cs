using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// The named rate-limit policy of the endpoints an export grant reaches (<see cref="RateLimitPolicies.MigrationPull"/>,
/// issue #127): a fixed window per client address, with no queue.
/// </summary>
/// <remarks>
/// Keyed by address and not by grant, because a grant is one a stranger may be guessing at and the budget that
/// means something is how fast one address may ask. It bounds what a LIVE grant can do; a wrong token is counted
/// earlier, by <see cref="MigrationGrantFailureLimiter"/>.
/// </remarks>
public static class MigrationPullRateLimitPolicy
{
    /// <summary>The policy's registered name.</summary>
    public const string Name = RateLimitPolicies.MigrationPull;

    /// <summary>Registers the policy on the limiter options.</summary>
    /// <param name="options">The rate-limiter options being configured.</param>
    /// <param name="rateLimitOptions">The panel's rate-limit settings.</param>
    public static void Configure(RateLimiterOptions options, RateLimitOptions rateLimitOptions)
    {
        options.AddPolicy(Name, context =>
        {
            return RateLimitPartition.GetFixedWindowLimiter(BuildPartitionKey(context), _ =>
            {
                return new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.MigrationPullMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.MigrationPullWindowSeconds),

                    // Zero, so a caller over the limit is refused immediately rather than parked.
                    QueueLimit = 0,
                    AutoReplenishment = true,
                };
            });
        });
    }

    /// <summary>The partition a request is counted in: its client address.</summary>
    /// <param name="context">The request.</param>
    /// <returns>The address in the panel's canonical spelling.</returns>
    private static string BuildPartitionKey(HttpContext context)
    {
        return ClientAddress.Of(context.Connection.RemoteIpAddress);
    }
}
