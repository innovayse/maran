using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// Invitation-sending rate limiting: its own bucket, partitioned per hosting account
/// (rules/security.md "Rate limiting is mandatory ... and any expensive operation").
/// </summary>
/// <remarks>
/// <para>
/// <b>Not <see cref="InvitationRateLimitPolicy"/>'s bucket.</b> That one bounds how fast one ADDRESS
/// may spend invitation tokens; this one bounds how much mail one ACCOUNT may make the panel send to
/// addresses its owner typed. Sharing a meter let either traffic starve the other — see
/// <c>RateLimitPolicies.InvitationSending</c>.
/// </para>
/// <para>
/// <b>Keyed by <see cref="RateLimitPartitionKey"/>, not by the address.</b> The endpoint is
/// authenticated, so the account is known before model binding, and an account is the unit whose
/// mail is being bounded: keyed by address, an owner on a shared NAT would be throttled by their
/// neighbours, and one owner with many addresses would get a budget per address. The key is never
/// read from the body, which the caller controls.
/// </para>
/// <para>
/// The bucket also bounds the enumeration channel that remains once the invitation answers
/// uniformly: whether a name exists is still visible to the owner in their later list, so the number
/// of names an owner can test per window is the number of invitations they may send.
/// </para>
/// </remarks>
public static class InvitationSendingRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.InvitationSending;

    /// <summary>Registers the policy on <paramref name="options"/>.</summary>
    /// <param name="options">The rate limiter options to add this policy to.</param>
    /// <param name="rateLimitOptions">Configured request count and window.</param>
    public static void Configure(RateLimiterOptions options, RateLimitOptions rateLimitOptions)
    {
        options.AddPolicy(Name, context =>
        {
            return RateLimitPartition.GetFixedWindowLimiter(RateLimitPartitionKey.For(context), _ =>
            {
                return new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.InvitationSendMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.InvitationSendWindowSeconds),

                    // Zero, so a caller over the limit is refused immediately rather than parked.
                    QueueLimit = 0,
                    AutoReplenishment = true,
                };
            });
        });
    }
}
