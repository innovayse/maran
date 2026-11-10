using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// The update-check limit: its own bucket, partitioned per signed-in administrator, for the request that
/// makes the root check service contact the release host (issue #516; rules/security.md "Rate limiting
/// is mandatory ... and any expensive operation").
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own policy, because what it bounds is an outbound fetch made as root.</b> The general API
/// budget is sized for reading screens, so a stolen administrator session looping on the button would be
/// allowed hundreds of requests a minute. This bucket allows ten an hour, which slows that session and
/// does not stop it; the one marker and the update lock behind it keep two checks from running at once,
/// and the threat note states the rest.
/// </para>
/// <para>
/// <b>Keyed by the authenticated user</b>, as the export policy is, and for the same reason: the budget
/// that means something is the person. A caller with no user id (anonymous, or an API key, both refused
/// later by the endpoint's policy) is keyed by address in the panel's one spelling
/// (<see cref="ClientAddress"/>). The queue is zero, so a caller over the limit is refused immediately.
/// </para>
/// </remarks>
public static class UpdateCheckRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.UpdateCheck;

    /// <summary>Registers the policy on <paramref name="options"/>.</summary>
    /// <param name="options">The rate limiter options to add this policy to.</param>
    /// <param name="rateLimitOptions">Configured request count and window.</param>
    public static void Configure(RateLimiterOptions options, RateLimitOptions rateLimitOptions)
    {
        ArgumentNullException.ThrowIfNull(rateLimitOptions);

        options.AddPolicy(Name, context =>
        {
            return RateLimitPartition.GetFixedWindowLimiter(PartitionKeyOf(context), _ =>
            {
                return new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.UpdateCheckMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.UpdateCheckWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                };
            });
        });
    }

    /// <summary>Builds the partition key: the signed-in user's id, or the caller's address when there is none.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>The key, prefixed with which of the two identities it names.</returns>
    private static string PartitionKeyOf(HttpContext context)
    {
        var userId = context.User.FindFirst(PanelClaimTypes.UserId)?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            return $"user:{userId}";
        }

        return $"ip:{ClientAddress.Of(context.Connection.RemoteIpAddress)}";
    }
}
