using System.ComponentModel.DataAnnotations;

namespace Maran.Host.Configuration;

/// <summary>
/// The bounds of the live dashboard stream (<c>GET /api/v1/dashboard/stream</c>, issue #199). Bound
/// from the <c>DashboardStream</c> configuration section and validated at startup, so a limit that is
/// zero or absurd fails the boot instead of silently removing the protection it names.
/// </summary>
/// <remarks>
/// Every figure here bounds what a stream may cost the server: how many may be open, how long one may
/// live, and how often a connection re-reads the panel's own sections. The cost of the host readings
/// themselves does not depend on any of them, because those come from one shared feed.
/// </remarks>
public sealed class DashboardStreamOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "DashboardStream";

    /// <summary>
    /// How many streams one signed-in user may hold open at once. A concurrency budget enforced by
    /// <c>DashboardStreamRateLimitPolicy</c>; four is a few tabs and a phone.
    /// </summary>
    [Range(1, 50)]
    public int PerUserStreamLimit { get; set; } = 4;

    /// <summary>
    /// How many streams the whole process may hold open at once, whoever opens them. Enforced by
    /// <c>DashboardStreamGate</c>.
    /// </summary>
    [Range(1, 1_000)]
    public int PerProcessStreamLimit { get; set; } = 64;

    /// <summary>How long a stream may be silent before a heartbeat comment, in seconds.</summary>
    /// <remarks>Below a reverse proxy's read timeout (nginx defaults to 60 seconds), which is why it exists.</remarks>
    [Range(1, 55)]
    public int HeartbeatSeconds { get; set; } = 15;

    /// <summary>
    /// The longest one stream lives, in seconds, before the server ends it with an <c>end</c> frame
    /// whose reason is <c>renew</c> and the client reopens it. Reopening re-checks the access token,
    /// which is what bounds how long a revoked token keeps a stream alive.
    /// </summary>
    [Range(30, 3_600)]
    public int MaxLifetimeSeconds { get; set; } = 900;

    /// <summary>
    /// How often a stream wakes to check its lifetime and which of its sections are due, in
    /// milliseconds. Bounds how late a lifetime end or a section refresh can be.
    /// </summary>
    [Range(100, 5_000)]
    public int TickMilliseconds { get; set; } = 1_000;

    /// <summary>How often a connection re-reads the service statuses, in seconds.</summary>
    [Range(1, 300)]
    public int ServicesIntervalSeconds { get; set; } = 30;

    /// <summary>How often a connection re-reads the counts and the attention block, in seconds.</summary>
    [Range(1, 300)]
    public int TotalsIntervalSeconds { get; set; } = 10;

    /// <summary>How often a connection re-reads the newest audit entries, in seconds.</summary>
    [Range(1, 300)]
    public int ActivityIntervalSeconds { get; set; } = 5;

    /// <summary><see cref="HeartbeatSeconds"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Heartbeat
    {
        get
        {
            return TimeSpan.FromSeconds(HeartbeatSeconds);
        }
    }

    /// <summary><see cref="MaxLifetimeSeconds"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan MaxLifetime
    {
        get
        {
            return TimeSpan.FromSeconds(MaxLifetimeSeconds);
        }
    }

    /// <summary><see cref="TickMilliseconds"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan Tick
    {
        get
        {
            return TimeSpan.FromMilliseconds(TickMilliseconds);
        }
    }

    /// <summary><see cref="ServicesIntervalSeconds"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan ServicesInterval
    {
        get
        {
            return TimeSpan.FromSeconds(ServicesIntervalSeconds);
        }
    }

    /// <summary><see cref="TotalsIntervalSeconds"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan TotalsInterval
    {
        get
        {
            return TimeSpan.FromSeconds(TotalsIntervalSeconds);
        }
    }

    /// <summary><see cref="ActivityIntervalSeconds"/> as a <see cref="TimeSpan"/>.</summary>
    public TimeSpan ActivityInterval
    {
        get
        {
            return TimeSpan.FromSeconds(ActivityIntervalSeconds);
        }
    }
}
