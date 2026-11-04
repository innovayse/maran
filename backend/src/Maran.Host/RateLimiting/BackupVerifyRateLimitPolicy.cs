using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// The backup-verification limit: its own bucket, partitioned per hosting account
/// (rules/security.md "Rate limiting is mandatory ... and any expensive operation").
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own policy, because what it bounds is disk I/O in the root agent.</b> One request makes the
/// agent read an entire backup archive and hash it. The general API budget is sized for reading
/// screens, so a caller looping on "Verify now" would be allowed hundreds of full-archive reads a
/// minute; this bucket allows ten an hour. It is not <see cref="BackupRestoreRateLimitPolicy"/>'s
/// number either: a verification only reads and is a normal thing to ask for several backups in a row.
/// </para>
/// <para>
/// <b>Keyed by the hosting ACCOUNT, through the shared <see cref="RateLimitPartitionKey"/></b>, for
/// the reason the restore policy gives: an account with several panel users must not multiply the
/// budget, and an administrator, who owns no account, has one budget across every customer.
/// </para>
/// <para>
/// The queue is zero, so a caller over the limit is refused immediately rather than parked. The
/// verification the panel starts itself after each backup run is not an HTTP request and is not
/// counted here.
/// </para>
/// </remarks>
public static class BackupVerifyRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.BackupVerify;

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
                    PermitLimit = rateLimitOptions.BackupVerifyMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.BackupVerifyWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                };
            });
        });
    }
}
