using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Utilities.Network;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// The journal-export limit: its own bucket, partitioned per signed-in administrator, shared by the
/// audit export and the personal-data export (rules/security.md "Rate limiting is mandatory ... and any
/// expensive operation").
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own policy, because what it bounds is how fast the security record can leave the panel.</b>
/// One request makes the database read up to the export cap and hands every address and user agent in
/// it to the caller. The general API budget is sized for reading screens, so a stolen administrator
/// session looping on the export would be allowed hundreds of them a minute; this bucket allows six an
/// hour. That slows such a session and does not stop it, which the threat note states.
/// </para>
/// <para>
/// <b>Keyed by the authenticated user, not by the shared <see cref="RateLimitPartitionKey"/>.</b> That
/// key prefers the hosting account when the token carries one, and an account is a unit several people
/// share. The exports are administrator-only and an administrator owns no account, so the budget that
/// means something here is the person: one administrator, one allowance, whatever address or token
/// claim they arrive with. A caller with no user id (anonymous, or an API key, both refused later by
/// the endpoint's policy) is keyed by address, the only thing known about them, in the panel's one
/// spelling (<see cref="ClientAddress"/>).
/// </para>
/// <para>
/// The queue is zero, so a caller over the limit is refused immediately rather than parked.
/// </para>
/// </remarks>
public static class AuditExportRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.AuditExport;

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
                    PermitLimit = rateLimitOptions.AuditExportMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.AuditExportWindowSeconds),
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
