using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// Invitation-acceptance rate limiting: its own bucket, partitioned per client address
/// (rules/security.md "Rate limiting is mandatory ... and any expensive operation").
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately not a reuse of <c>PasswordResetRateLimitPolicy</c>'s bucket.</b> Both endpoints
/// are anonymous and both spend a token, which made sharing the bucket look free until the failure
/// modes were worked out: a customer behind shared NAT whose neighbours are requesting password
/// resets would find their one-time invitation link refused, with no self-service recovery, because
/// resending an invitation is an administrator-only action — unlike asking for another reset mail.
/// And in the other direction, an attacker probing invitation tokens from one address would burn the
/// reset budget of every genuine customer sharing it. Accepting an invitation is a one-time
/// onboarding step; a password reset is recovery traffic that recurs for the life of the account —
/// the two must not share a meter in either direction.
/// </para>
/// <para>
/// <b>Partitioned by the address and by nothing else — the same lesson
/// <c>LoginRateLimitPolicy</c> and <c>PasswordResetRateLimitPolicy</c> record.</b> The resolver runs
/// before model binding and must not read the body, so the token being spent is not available to
/// it. That is just as well: it is a value the caller supplies, and a limiter whose key the caller
/// controls bounds nothing.
/// </para>
/// </remarks>
public static class InvitationRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.Invitation;

    /// <summary>Registers the policy on <paramref name="options"/>.</summary>
    /// <param name="options">The rate limiter options to add this policy to.</param>
    /// <param name="rateLimitOptions">Configured request count and window.</param>
    public static void Configure(RateLimiterOptions options, RateLimitOptions rateLimitOptions)
    {
        options.AddPolicy(Name, context =>
        {
            return RateLimitPartition.GetFixedWindowLimiter(BuildPartitionKey(context), _ =>
            {
                return new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.InvitationMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.InvitationWindowSeconds),

                    // Zero, so a caller over the limit is refused immediately rather than parked.
                    // A queue here would hold request threads open for exactly the caller who is
                    // abusing the endpoint.
                    QueueLimit = 0,
                    AutoReplenishment = true,
                };
            });
        });
    }

    /// <summary>Builds the partition key: the caller's address in the panel's canonical spelling.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>The partition every invitation-acceptance request from this address shares.</returns>
    /// <remarks>
    /// Normalised through <see cref="ClientAddress"/>, so an IPv4 caller reported by a dual-stack
    /// socket as <c>::ffff:a.b.c.d</c> and the same caller reported plainly by the reverse proxy
    /// share one bucket rather than getting two budgets.
    /// </remarks>
    private static string BuildPartitionKey(HttpContext context)
    {
        return ClientAddress.Of(context.Connection.RemoteIpAddress);
    }
}
