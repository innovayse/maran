using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// Password-reset completion rate limiting: its own bucket, partitioned per client address, for the
/// endpoint that spends a reset token (issue #492).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it is not <see cref="PasswordResetRateLimitPolicy"/>.</b> That policy meters requests that
/// make the panel send mail; this one meters attempts to spend a token, which send nothing. In one
/// bucket a flood of reset requests from an address blocked the legitimate completion of the reset
/// the person had just been mailed, and completion attempts were not limited on their own terms.
/// </para>
/// <para>
/// <b>Partitioned by the address alone.</b> The token and the account it names are in the request
/// body, which the limiter's resolver runs before binding and must not read; and a key the caller can
/// vary at will (the token) bounds nothing. Address is the key all the anonymous auth policies use.
/// </para>
/// </remarks>
public static class PasswordResetCompletionRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.PasswordResetCompletion;

    /// <summary>Registers the policy on <paramref name="options"/>.</summary>
    /// <param name="options">The rate limiter options to add this policy to.</param>
    /// <param name="rateLimitOptions">Configured request count and window.</param>
    public static void Configure(RateLimiterOptions options, RateLimitOptions rateLimitOptions)
    {
        options.AddPolicy(Name, context =>
        {
            return RateLimitPartition.GetFixedWindowLimiter(ClientAddress.Of(context.Connection.RemoteIpAddress), _ =>
            {
                return new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.PasswordResetCompletionMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.PasswordResetCompletionWindowSeconds),

                    // Zero, so a caller over the limit is refused immediately rather than parked.
                    QueueLimit = 0,
                    AutoReplenishment = true,
                };
            });
        });
    }
}
