using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// The provisioning limit: its own fixed window, partitioned per API key, bounding how fast one
/// integration may create, change or delete hosting accounts
/// (rules/csharp.md "Rate limiting is mandatory on ... the provisioning API").
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own policy rather than a number on <see cref="ApiRateLimitPolicy"/>.</b> What it bounds is
/// what a stolen secret can DO, not load: every permitted request writes a hosting account. Sharing the
/// general budget would let a reading integration spend the allowance of the one that provisions, and
/// the reverse.
/// </para>
/// <para>
/// <b>Keyed by the key, through the shared <see cref="RateLimitPartitionKey"/>.</b> A principal the API
/// key handler authenticated is keyed by its key id before any other identity, so two integrations
/// behind one address have a budget each, and a key used from many addresses has one. A request that is
/// not a key (a session token, which the key policies refuse) falls to the shared key's other arms and
/// cannot spend a key's budget. The queue is zero: a caller over the limit is refused at once, because
/// parking would hold open exactly the request that is repeating a write.
/// </para>
/// </remarks>
public static class ProvisioningRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.Provisioning;

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
                    PermitLimit = rateLimitOptions.ProvisioningMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.ProvisioningWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                };
            });
        });
    }
}
