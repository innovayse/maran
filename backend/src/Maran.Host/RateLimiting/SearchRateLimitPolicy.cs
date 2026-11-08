using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// The global-search limit: its own fixed window, partitioned per signed-in user, so the palette's
/// keystroke-paced requests can neither exhaust the general API budget nor be starved by it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Keyed by the authenticated user, not by the shared <see cref="RateLimitPartitionKey"/>.</b> That
/// key prefers the hosting account, a unit several people share; what a search budget bounds is one
/// person's typing, and an account with five panel users would otherwise get five times the allowance of
/// a single user. A caller with no user id (anonymous, refused later by the endpoint's policy) is keyed by
/// address in the panel's one spelling (<see cref="ClientAddress"/>).
/// </para>
/// <para>
/// It also bounds the existence-oracle risk the threat note names: a script walking the namespace one
/// term at a time is held to this rate. The queue is zero, so a caller over the limit is refused at once.
/// </para>
/// </remarks>
public static class SearchRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>RequireRateLimiting</c>.</summary>
    public const string Name = RateLimitPolicies.Search;

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
                    PermitLimit = rateLimitOptions.SearchMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.SearchWindowSeconds),
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
