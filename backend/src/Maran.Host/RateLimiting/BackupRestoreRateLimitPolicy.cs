using System.Threading.RateLimiting;
using Maran.Host.Configuration;
using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.RateLimiting;

namespace Maran.Host.RateLimiting;

/// <summary>
/// The restore limit: its own bucket, partitioned per hosting account
/// (rules/security.md "Rate limiting is mandatory ... and any expensive operation").
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own policy rather than a tighter number on <see cref="ApiRateLimitPolicy"/>, because what
/// it bounds is not load.</b> A restore replaces a live account: it swaps the home directory and
/// drops every database before reloading it, and it can take hours. The general API budget is sized
/// for reading screens, so a restore sharing it would either be effectively unlimited or would make
/// the panel unusable for everyone who was not restoring. What this limit is for is the OPERATION's
/// own hazard — a caller repeating a restore over an account that is already being restored, or
/// walking backwards through a list of archives one click at a time — and three attempts an hour is
/// generous for a thing a person does deliberately and never does twice by accident.
/// </para>
/// <para>
/// <b>Keyed by the hosting ACCOUNT, through the shared
/// <see cref="RateLimitPartitionKey"/>.</b> The account is the thing being replaced, so an account
/// with five panel users must not get five times the budget — the resource at risk is one home
/// directory, not five people's patience. It is the CALLER'S account and not the target's, which
/// matters for an administrator: an administrator owns no account, falls to the per-user key, and
/// therefore has one budget across every customer they restore. That is the intended reading — an
/// operator restoring three accounts in an hour is doing something worth pausing over — and it is
/// stated because the alternative reading, "per target account", is the one somebody will assume.
/// </para>
/// <para>
/// The queue is zero, so a caller over the limit is refused immediately rather than parked. Parking
/// would hold a request open for exactly the caller who is repeating a destructive operation.
/// </para>
/// </remarks>
public static class BackupRestoreRateLimitPolicy
{
    /// <summary>The policy name endpoints enable with <c>[EnableRateLimiting]</c>.</summary>
    public const string Name = RateLimitPolicies.BackupRestore;

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
                    PermitLimit = rateLimitOptions.BackupRestoreMaxRequests,
                    Window = TimeSpan.FromSeconds(rateLimitOptions.BackupRestoreWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                };
            });
        });
    }
}
