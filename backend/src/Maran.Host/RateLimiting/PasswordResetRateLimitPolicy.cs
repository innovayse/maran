using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// Password-reset rate limiting: its own bucket, partitioned per client address
/// (rules/security.md "Rate limiting is mandatory ... and any expensive operation").
/// </summary>
/// <remarks>
/// <para>
/// <b>What this endpoint spends is not a guess but an outgoing message.</b> An unlimited reset
/// endpoint is a mail bomb with the operator's own return address on it: a caller names any address
/// they like and the panel sends to it, as fast as the loop runs. The consequences are the victim's
/// inbox and the panel's domain being listed as a spam source, neither of which the account owner
/// did anything to earn.
/// </para>
/// <para>
/// <b>Partitioned by the address and by nothing else — the same lesson
/// <c>LoginRateLimitPolicy</c> records.</b> The resolver runs before model binding and must not read
/// the body, so the e-mail being requested is not available to it. That is just as well: it is a
/// value the caller chooses, and a limiter whose key the caller can change at will bounds nothing.
/// </para>
/// <para>
/// The cost of an address-only key is that callers behind one NAT share a budget. Three requests per
/// fifteen minutes is generous for people who have genuinely forgotten a password and are looking at
/// their mail, and the alternative — a key the caller supplies — is not a limit.
/// </para>
/// </remarks>
public static class PasswordResetRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.PasswordReset;

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
                    PermitLimit = rateLimitOptions.PasswordResetMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.PasswordResetWindowSeconds),

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
    /// <returns>The partition every reset request from this address shares.</returns>
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
